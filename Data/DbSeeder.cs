using ARIS1.Models;
using Microsoft.AspNetCore.Identity;

namespace ARIS1.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();

            // Create roles if they don't exist
            string[] roles = { "Admin", "Teacher", "Learner" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // Create default admin account if it doesn't exist
            string adminEmail = "admin@aris.com";
            string adminPassword = "Admin@1234";

            var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
            if (existingAdmin == null)
            {
                var adminUser = new User
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    Fullname = "System Administrator",
                    Role = "Admin",
                    IsActive = true,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(adminUser, "Admin");
            }
            else
            {
                // Make sure existing admin has confirmed email
                if (!existingAdmin.EmailConfirmed)
                {
                    existingAdmin.EmailConfirmed = true;
                    await userManager.UpdateAsync(existingAdmin);
                }
            }
        }
    }
}