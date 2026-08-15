using System.Security.Claims;
using ARIS1.Models;
using Microsoft.AspNetCore.Identity;

namespace ARIS1.Services
{
    public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<User, IdentityRole>
    {
        public AppUserClaimsPrincipalFactory(
            UserManager<User> userManager,
            RoleManager<IdentityRole> roleManager,
            Microsoft.Extensions.Options.IOptions<IdentityOptions> optionsAccessor)
            : base(userManager, roleManager, optionsAccessor)
        {
        }

        public override async Task<ClaimsPrincipal> CreateAsync(User user)
        {
            var principal = await base.CreateAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;

            if (!string.IsNullOrWhiteSpace(user.Fullname))
                identity.AddClaim(new Claim("Fullname", user.Fullname));

            if (user.SchoolId.HasValue)
                identity.AddClaim(new Claim("SchoolId", user.SchoolId.Value.ToString()));

            return principal;
        }
    }
}