using System.Text;
using ARIS1.Data;
using ARIS1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services.Email
{
    // Welcome / reset-link emails triggered by staff (Create User, Bulk Import, SuperAdmin Admins, Reset
    // Password). Always a one-time set-password link built exactly like ForgotPassword.razor, never a password.
    public class AccountEmailService
    {
        private readonly UserManager<User> _userManager;
        private readonly EmailService _emailService;
        private readonly AppDbContext _dbContext;

        public AccountEmailService(UserManager<User> userManager, EmailService emailService, AppDbContext dbContext)
        {
            _userManager = userManager;
            _emailService = emailService;
            _dbContext = dbContext;
        }

        public async Task<EmailLog> SendWelcomeAsync(User user, string role, string baseUri, string? sentByUserId)
        {
            var link = await BuildResetLinkAsync(user, baseUri);
            var school = user.SchoolId == null
                ? null
                : await _dbContext.Schools.Where(s => s.SchoolId == user.SchoolId).Select(s => new { s.Name, s.Code }).FirstOrDefaultAsync();
            var rendered = EmailTemplates.Welcome(user.Fullname, role, user.Email ?? string.Empty, school?.Code, link, school?.Name);
            return await QueueAsync(user, rendered, sentByUserId);
        }

        public async Task<EmailLog> SendResetLinkAsync(User user, string baseUri, string? sentByUserId)
        {
            var link = await BuildResetLinkAsync(user, baseUri);
            var rendered = EmailTemplates.PasswordReset(user.Fullname, link, await SchoolNameAsync(user));
            return await QueueAsync(user, rendered, sentByUserId);
        }

        // Verification link for users who already have a password. Account/ConfirmEmail marks the address confirmed.
        public async Task<EmailLog> SendVerificationAsync(User user, string baseUri, string? sentByUserId)
        {
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var link = QueryHelpers.AddQueryString(new Uri(new Uri(baseUri), "Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });
            var rendered = EmailTemplates.ConfirmEmail(user.Fullname, link, await SchoolNameAsync(user));
            return await QueueAsync(user, rendered, sentByUserId);
        }

        private async Task<string> BuildResetLinkAsync(User user, string baseUri)
        {
            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            return QueryHelpers.AddQueryString(new Uri(new Uri(baseUri), "Account/ResetPassword").AbsoluteUri, "code", code);
        }

        private Task<EmailLog> QueueAsync(User user, RenderedEmail rendered, string? sentByUserId) =>
            _emailService.QueueAsync(new EmailMessage(user.Email ?? string.Empty, rendered.Subject, rendered.Html, rendered.Text,
                EmailCategories.Account, user.SchoolId, user.Id, SentByUserId: sentByUserId));

        private async Task<string?> SchoolNameAsync(User user) =>
            user.SchoolId == null
                ? null
                : await _dbContext.Schools.Where(s => s.SchoolId == user.SchoolId).Select(s => s.Name).FirstOrDefaultAsync();
    }
}
