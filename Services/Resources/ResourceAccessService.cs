using System.Security.Claims;
using ARIS1.Data;
using ARIS1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services.Resources
{
    public record ResourceSubject(int SubjectId, string Name, int Grade, int SchoolId);

    // Who can see and manage learning resources. Deny by default; every page and the download endpoint go through
    // here rather than trusting route/query/bound ids. All subject lists are scoped to the school's current
    // academic year (MAX(Subject.AcademicYear), same as the Teacher pages), so rolled-over years don't blend in.
    //   Teacher: manage + view own current-year subjects
    //   Learner: view enrolled current-year subjects
    //   Parent:  view a linked child's current-year subjects
    //   Admin:   manage (moderate) + view every current-year subject in their school
    public class ResourceAccessService
    {
        private readonly AppDbContext _dbContext;
        private readonly UserManager<User> _userManager;
        private readonly SchoolAuthorizationService _schoolAuth;

        public ResourceAccessService(AppDbContext dbContext, UserManager<User> userManager, SchoolAuthorizationService schoolAuth)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _schoolAuth = schoolAuth;
        }

        public async Task<int> CurrentYearAsync(int schoolId) =>
            await _dbContext.Subjects
                .Where(s => s.SchoolId == schoolId)
                .Select(s => (int?)s.AcademicYear)
                .MaxAsync() ?? DateTime.Now.Year;

        // Subjects whose resources this user may view. For a parent, learnerId selects which linked child.
        public async Task<List<ResourceSubject>> GetViewableSubjectsAsync(ClaimsPrincipal principal, int? learnerId = null)
        {
            var user = await _userManager.GetUserAsync(principal);
            if (user?.SchoolId == null || !user.IsActive) return new List<ResourceSubject>();
            var schoolId = user.SchoolId.Value;
            var currentYear = await CurrentYearAsync(schoolId);

            IQueryable<Subject> query;
            if (principal.IsInRole("Admin"))
            {
                query = _dbContext.Subjects.Where(s => s.SchoolId == schoolId);
            }
            else if (principal.IsInRole("Teacher"))
            {
                var teacherId = await _dbContext.Teachers.Where(t => t.UserId == user.Id).Select(t => (int?)t.TeacherId).FirstOrDefaultAsync();
                if (teacherId == null) return new List<ResourceSubject>();
                query = _dbContext.Subjects.Where(s => s.SchoolId == schoolId && s.TeacherId == teacherId);
            }
            else if (principal.IsInRole("Learner"))
            {
                var ownLearnerId = await _dbContext.Learners.Where(l => l.UserId == user.Id).Select(l => (int?)l.LearnerId).FirstOrDefaultAsync();
                if (ownLearnerId == null) return new List<ResourceSubject>();
                query = _dbContext.LearnerSubjects.Where(ls => ls.LearnerId == ownLearnerId).Select(ls => ls.Subject);
            }
            else if (principal.IsInRole("Parent"))
            {
                if (learnerId == null || !await _schoolAuth.HasAccessToLearner(user.UserName, learnerId.Value))
                    return new List<ResourceSubject>();
                query = _dbContext.LearnerSubjects.Where(ls => ls.LearnerId == learnerId.Value).Select(ls => ls.Subject);
            }
            else
            {
                return new List<ResourceSubject>();
            }

            return await query
                .Where(s => s.SchoolId == schoolId && s.AcademicYear == currentYear)
                .Distinct()
                .OrderBy(s => s.Grade).ThenBy(s => s.Name)
                .Select(s => new ResourceSubject(s.SubjectId, s.Name, s.Grade, s.SchoolId))
                .ToListAsync();
        }

        public async Task<bool> CanManageAsync(ClaimsPrincipal principal, int subjectId)
        {
            if (!principal.IsInRole("Teacher") && !principal.IsInRole("Admin")) return false;
            var subjects = await GetViewableSubjectsAsync(principal);
            return subjects.Any(s => s.SubjectId == subjectId);
        }

        public async Task<bool> CanViewAsync(ClaimsPrincipal principal, LearningResource resource)
        {
            if (await CanManageAsync(principal, resource.SubjectId)) return true;
            if (!resource.IsActive) return false;

            if (principal.IsInRole("Parent"))
            {
                var user = await _userManager.GetUserAsync(principal);
                var childIds = await _schoolAuth.GetAccessibleLearnerIds(user?.UserName);
                foreach (var childId in childIds)
                {
                    if ((await GetViewableSubjectsAsync(principal, childId)).Any(s => s.SubjectId == resource.SubjectId))
                        return true;
                }
                return false;
            }

            return (await GetViewableSubjectsAsync(principal)).Any(s => s.SubjectId == resource.SubjectId);
        }

        // Absolute http/https URLs only (blocks javascript:, data:, relative paths).
        public static bool TryNormaliseUrl(string? input, out string url)
        {
            url = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;
            if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            url = uri.AbsoluteUri;
            return url.Length <= 2000;
        }
    }
}
