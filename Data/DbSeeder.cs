using ARIS1.Models;
using Microsoft.AspNetCore.Identity;

namespace ARIS1.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<AppDbContext>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();

            // Create roles if they don't exist
            string[] roles = { "SuperAdmin", "Admin", "Teacher", "Learner" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // Create default school if it doesn't exist
            if (!context.Schools.Any())
            {
                var defaultSchool = new School
                {
                    Name = "Default School",
                    Code = "DEFAULT",
                    Address = "123 Main Street",
                    Email = "info@school.com",
                    Phone = "555-0100",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    LastModifiedDate = DateTime.UtcNow
                };
                context.Schools.Add(defaultSchool);
                await context.SaveChangesAsync();

                // Assign this school to all existing subjects
                var subjects = context.Subjects.ToList();
                foreach (var subject in subjects)
                {
                    subject.SchoolId = defaultSchool.SchoolId;
                }
                await context.SaveChangesAsync();
            }

            // Create default SuperAdmin account if it doesn't exist
            string superAdminEmail = "superadmin@aris.com";
            string superAdminPassword = "SuperAdmin@1234";

            var existingSuperAdmin = await userManager.FindByEmailAsync(superAdminEmail);
            if (existingSuperAdmin == null)
            {
                var superAdminUser = new User
                {
                    UserName = superAdminEmail,
                    Email = superAdminEmail,
                    Fullname = "Super Administrator",
                    Role = "SuperAdmin",
                    IsActive = true,
                    EmailConfirmed = true,
                    SchoolId = null // SuperAdmin doesn't belong to any school
                };

                var result = await userManager.CreateAsync(superAdminUser, superAdminPassword);
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(superAdminUser, "SuperAdmin");
            }

            // Create default admin account if it doesn't exist
            string adminEmail = "admin@aris.com";
            string adminPassword = "Admin@1234";

            var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
            if (existingAdmin == null)
            {
                // Get the default school
                var defaultSchool = context.Schools.First();

                var adminUser = new User
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    Fullname = "School Administrator",
                    Role = "Admin",
                    IsActive = true,
                    EmailConfirmed = true,
                    SchoolId = defaultSchool.SchoolId // Admin belongs to default school
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(adminUser, "Admin");
            }
            else
            {
                // Make sure existing admin has confirmed email and assigned to default school
                if (!existingAdmin.EmailConfirmed)
                {
                    existingAdmin.EmailConfirmed = true;
                    await userManager.UpdateAsync(existingAdmin);
                }
                if (existingAdmin.SchoolId == null && existingAdmin.Role == "Admin")
                {
                    var defaultSchool = context.Schools.First();
                    existingAdmin.SchoolId = defaultSchool.SchoolId;
                    await userManager.UpdateAsync(existingAdmin);
                }
            }
        }
    }
}