using ARIS1.Models;
using ARIS1.Data;
using Microsoft.AspNetCore.Identity;

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
            if (user?.Role == "SuperAdmin")
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
    }
}