using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ARIS1.Data;
using ARIS1.Models;
using ClosedXML.Excel;
using CsvHelper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public record ImportRowResult(int RowNumber, string FullName, string Email, string Role, bool Success, string? ErrorMessage, string? GeneratedPassword);

    public class BulkUserImportService
    {
        private readonly UserManager<User> _userManager;
        private readonly AppDbContext _dbContext;

        public BulkUserImportService(UserManager<User> userManager, AppDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }

        public static readonly string[] TemplateHeaders = { "FullName", "Email", "Role", "Grade", "ClassName", "Password" };

        public List<Dictionary<string, string>> ParseFile(Stream stream, string fileExtension)
        {
            return fileExtension.ToLowerInvariant() switch
            {
                ".csv" => ParseCsv(stream),
                ".xlsx" or ".xls" => ParseExcel(stream),
                _ => throw new NotSupportedException("Unsupported file type. Please upload a .csv or .xlsx file.")
            };
        }

        private static List<Dictionary<string, string>> ParseCsv(Stream stream)
        {
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var rows = new List<Dictionary<string, string>>();
            if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord == null)
                return rows;

            var headers = csv.HeaderRecord;
            while (csv.Read())
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in headers)
                {
                    row[header.Trim()] = (csv.GetField(header) ?? string.Empty).Trim();
                }
                rows.Add(row);
            }
            return rows;
        }

        private static List<Dictionary<string, string>> ParseExcel(Stream stream)
        {
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.First();
            var rows = new List<Dictionary<string, string>>();

            var usedRows = worksheet.RowsUsed().ToList();
            if (usedRows.Count == 0) return rows;

            var headerRow = usedRows[0];
            var headers = headerRow.CellsUsed().Select(c => c.GetString().Trim()).ToList();

            foreach (var row in usedRows.Skip(1))
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++)
                {
                    dict[headers[i]] = row.Cell(i + 1).GetString().Trim();
                }
                rows.Add(dict);
            }
            return rows;
        }

        public async Task<List<ImportRowResult>> ImportAsync(List<Dictionary<string, string>> rows, int schoolId)
        {
            var results = new List<ImportRowResult>();
            var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int rowNumber = 1; // row 1 is the header

            foreach (var row in rows)
            {
                rowNumber++;

                var fullName = row.GetValueOrDefault("FullName", string.Empty).Trim();
                var email = row.GetValueOrDefault("Email", string.Empty).Trim();
                var role = row.GetValueOrDefault("Role", string.Empty).Trim();
                var gradeRaw = row.GetValueOrDefault("Grade", string.Empty).Trim();
                var className = row.GetValueOrDefault("ClassName", string.Empty).Trim();
                var password = row.GetValueOrDefault("Password", string.Empty).Trim();

                if (row.Values.All(string.IsNullOrWhiteSpace)) continue; // skip blank rows

                var validationError = ValidateRow(fullName, email, role, gradeRaw, className, seenEmails);
                if (validationError != null)
                {
                    results.Add(new ImportRowResult(rowNumber, fullName, email, role, false, validationError, null));
                    continue;
                }

                seenEmails.Add(email);
                var normalizedRole = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase) ? "Teacher" : "Learner";

                SchoolClass? matchedClass = null;
                if (normalizedRole == "Learner")
                {
                    var gradeValue = int.Parse(gradeRaw);
                    matchedClass = await ResolveClassAsync(schoolId, gradeValue, className);
                    if (matchedClass == null)
                    {
                        results.Add(new ImportRowResult(rowNumber, fullName, email, normalizedRole, false,
                            $"No class '{className}' exists for Grade {gradeValue} at this school. Create it under Class Management first.", null));
                        continue;
                    }
                }

                var existingUser = await _userManager.FindByEmailAsync(email);
                if (existingUser != null)
                {
                    results.Add(new ImportRowResult(rowNumber, fullName, email, normalizedRole, false, "Email already in use.", null));
                    continue;
                }

                var passwordGenerated = string.IsNullOrEmpty(password);
                if (passwordGenerated) password = PasswordGenerator.Generate();

                var user = new User
                {
                    UserName = email,
                    Email = email,
                    Fullname = fullName,
                    IsActive = true,
                    EmailConfirmed = true,
                    SchoolId = schoolId
                };

                var createResult = await _userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    results.Add(new ImportRowResult(rowNumber, fullName, email, normalizedRole, false,
                        string.Join(", ", createResult.Errors.Select(e => e.Description)), null));
                    continue;
                }

                try
                {
                    await _userManager.AddToRoleAsync(user, normalizedRole);

                    if (normalizedRole == "Teacher")
                    {
                        _dbContext.Teachers.Add(new Teacher { UserId = user.Id });
                    }
                    else
                    {
                        _dbContext.Learners.Add(new Learner
                        {
                            UserId = user.Id,
                            Grade = int.Parse(gradeRaw),
                            ClassId = matchedClass!.ClassId,
                            EnrollmentYear = DateTime.Now.Year
                        });
                    }
                    await _dbContext.SaveChangesAsync();

                    results.Add(new ImportRowResult(rowNumber, fullName, email, normalizedRole, true, null,
                        passwordGenerated ? password : null));
                }
                catch (Exception ex)
                {
                    results.Add(new ImportRowResult(rowNumber, fullName, email, normalizedRole, false,
                        $"Account created but role setup failed: {ex.Message}", null));
                }
            }

            return results;
        }

        private static string? ValidateRow(string fullName, string email, string role, string gradeRaw, string className, HashSet<string> seenEmails)
        {
            if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2)
                return "Full name is required (min 2 characters).";

            if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
                return "A valid email address is required.";

            if (seenEmails.Contains(email))
                return "Duplicate email within the import file.";

            if (!string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Learner", StringComparison.OrdinalIgnoreCase))
                return "Role must be 'Teacher' or 'Learner'.";

            if (string.Equals(role, "Learner", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(gradeRaw, out var grade) || grade < 10 || grade > 12)
                    return "Grade must be 10, 11, or 12 for learners.";

                if (string.IsNullOrWhiteSpace(className))
                    return "ClassName is required for learners.";
            }

            return null;
        }

        // Tolerates both the short form ("A") and the legacy long form ("10A") for the class
        // name in the CSV, since existing spreadsheets from before this feature used the latter.
        private async Task<SchoolClass?> ResolveClassAsync(int schoolId, int grade, string className)
        {
            var name = className.Trim();
            var gradeDigits = grade.ToString();

            var match = await _dbContext.SchoolClasses
                .FirstOrDefaultAsync(c => c.SchoolId == schoolId && c.Grade == grade && c.Name.ToUpper() == name.ToUpper());
            if (match != null) return match;

            if (name.StartsWith(gradeDigits, StringComparison.OrdinalIgnoreCase) && name.Length > gradeDigits.Length)
            {
                var shortName = name.Substring(gradeDigits.Length);
                match = await _dbContext.SchoolClasses
                    .FirstOrDefaultAsync(c => c.SchoolId == schoolId && c.Grade == grade && c.Name.ToUpper() == shortName.ToUpper());
            }

            return match;
        }
    }
}
