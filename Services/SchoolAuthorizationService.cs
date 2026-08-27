using ARIS1.Models;
using ARIS1.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public class SchoolAuthorizationService
    {
        private readonly UserManager<User> _userManager;
        private readonly AppDbContext _context;

        public SchoolAuthorizationService(UserManager<User> userManager, AppDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        /// <summary>
        /// Gets the current user's school ID. Returns null for SuperAdmin.
        /// </summary>
        public async Task<int?> GetCurrentUserSchoolId(string? userName)
        {
            if (string.IsNullOrEmpty(userName))
                return null;

            var user = await _userManager.FindByNameAsync(userName);
            return user?.SchoolId;
        }

        /// <summary>
        /// Validates that the user has access to a specific school.
        /// SuperAdmin has access to all schools.
        /// </summary>
        public async Task<bool> HasAccessToSchool(string? userName, int schoolId)
        {
            if (string.IsNullOrEmpty(userName))
                return false;

            var user = await _userManager.FindByNameAsync(userName);

            // SuperAdmin has access to all
            if (user != null && await _userManager.IsInRoleAsync(user, "SuperAdmin"))
                return true;

            // Others must belong to the school
            return user?.SchoolId == schoolId;
        }

        /// <summary>
        /// Validates that the user has access to a specific subject.
        /// </summary>
        public async Task<bool> HasAccessToSubject(string? userName, int subjectId)
        {
            if (string.IsNullOrEmpty(userName))
                return false;

            var subject = await _context.Subjects.FindAsync(subjectId);
            if (subject == null)
                return false;

            return await HasAccessToSchool(userName, subject.SchoolId);
        }

        /// <summary>
        /// Returns the LearnerIds this parent is linked to, via the ParentLearner table.
        /// Empty list if the user isn't found, isn't in the Parent role, or has no linked
        /// children — never throws.
        /// </summary>
        public async Task<List<int>> GetAccessibleLearnerIds(string? userName)
        {
            if (string.IsNullOrEmpty(userName))
                return new List<int>();

            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
                return new List<int>();

            // Defense in depth: every Parent-facing page will also be gated by
            // [Authorize(Roles = "Parent")], but don't let a stale Parent/ParentLearner
            // row grant access if this user's role assignment has since changed.
            if (!await _userManager.IsInRoleAsync(user, "Parent"))
                return new List<int>();

            var parent = await _context.Parents
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (parent == null)
                return new List<int>();

            return await _context.ParentLearners
                .AsNoTracking()
                .Where(pl => pl.ParentId == parent.ParentId)
                .Select(pl => pl.LearnerId)
                .ToListAsync();
        }

        /// <summary>
        /// Validates that the given user (must be in the Parent role) is linked to the specific
        /// learner via the ParentLearner table. Deny-by-default. This is a resource-level check
        /// and does not replace [Authorize(Roles = "Parent")] on the page — both are required
        /// together, the same way HasAccessToSubject is used alongside role attributes elsewhere
        /// in this codebase.
        /// </summary>
        public async Task<bool> HasAccessToLearner(string? userName, int learnerId)
        {
            var accessibleIds = await GetAccessibleLearnerIds(userName);
            return accessibleIds.Contains(learnerId);
        }
    }
}