using ARIS1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ARIS1.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            var context = serviceProvider.GetRequiredService<AppDbContext>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var env = serviceProvider.GetRequiredService<IHostEnvironment>();
            bool isDevelopment = env.IsDevelopment();

            // Create roles if they don't exist
            string[] roles = { "SuperAdmin", "Admin", "Teacher", "Learner", "Parent" };
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

                // Assign this school to any subjects that have no school yet
                var subjects = context.Subjects.Where(s => s.SchoolId == 0).ToList();
                foreach (var subject in subjects)
                {
                    subject.SchoolId = defaultSchool.SchoolId;
                }
                await context.SaveChangesAsync();
            }

            // Create default SuperAdmin account if it doesn't exist
            string superAdminEmail = "superadmin@aris.com";
            string superAdminPassword = configuration["SeedCredentials:SuperAdminPassword"]
                ?? (isDevelopment
                    ? "SuperAdmin@1234"
                    : throw new InvalidOperationException(
                        "SeedCredentials:SuperAdminPassword is not set. " +
                        "Set the SEEDCREDENTIALS__SUPERADMINPASSWORD environment variable."));

            var existingSuperAdmin = await userManager.FindByEmailAsync(superAdminEmail);
            if (existingSuperAdmin == null)
            {
                var superAdminUser = new User
                {
                    UserName = superAdminEmail,
                    Email = superAdminEmail,
                    Fullname = "Super Administrator",
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
            string adminPassword = configuration["SeedCredentials:AdminPassword"]
                ?? (isDevelopment
                    ? "Admin@1234"
                    : throw new InvalidOperationException(
                        "SeedCredentials:AdminPassword is not set. " +
                        "Set the SEEDCREDENTIALS__ADMINPASSWORD environment variable."));

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
                if (existingAdmin.SchoolId == null && await userManager.IsInRoleAsync(existingAdmin, "Admin"))
                {
                    var defaultSchool = context.Schools.First();
                    existingAdmin.SchoolId = defaultSchool.SchoolId;
                    await userManager.UpdateAsync(existingAdmin);
                }
            }
        }
    }
}