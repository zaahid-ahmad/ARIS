using Microsoft.AspNetCore.Identity;

namespace ARIS1.Models
{
    public class User : IdentityUser
    {
        public string Fullname { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        // School FK - nullable for SuperAdmin users
        public int? SchoolId { get; set; }
        public School? School { get; set; }
    }
}