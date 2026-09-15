using System.Net;
using ARIS1.Data;
using ARIS1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services.Email
{
    // Replaces the scaffolded NoOpEmailSender so Identity's built-in flows (Forgot Password, email
    // confirmation) actually send email through EmailService.
    public class IdentityEmailSender : IEmailSender<User>
    {
        private readonly EmailService _emailService;
        private readonly AppDbContext _dbContext;

        public IdentityEmailSender(EmailService emailService, AppDbContext dbContext)
        {
            _emailService = emailService;
            _dbContext = dbContext;
        }

        // Identity pages pass links already HTML-encoded; decode so the templates encode them exactly once.
        public async Task SendPasswordResetLinkAsync(User user, string email, string resetLink)
        {
            var rendered = EmailTemplates.PasswordReset(user.Fullname, WebUtility.HtmlDecode(resetLink), await SchoolNameAsync(user));
            await QueueAsync(user, email, rendered);
        }

        public async Task SendConfirmationLinkAsync(User user, string email, string confirmationLink)
        {
            var rendered = EmailTemplates.ConfirmEmail(user.Fullname, WebUtility.HtmlDecode(confirmationLink), await SchoolNameAsync(user));
            await QueueAsync(user, email, rendered);
        }

        public async Task SendPasswordResetCodeAsync(User user, string email, string resetCode)
        {
            var school = await SchoolNameAsync(user);
            var html = EmailTemplates.Layout("Your password reset code", school,
                $"<p style=\"margin:0 0 14px\">Your ARIS password reset code is:</p><p style=\"font-size:22px;font-weight:700\">{EmailTemplates.Encode(resetCode)}</p>",
                notificationFooter: false);
            await QueueAsync(user, email, new RenderedEmail("Your ARIS password reset code", html, $"Your ARIS password reset code is: {resetCode}"));
        }

        private Task QueueAsync(User user, string email, RenderedEmail rendered) =>
            _emailService.QueueAsync(new EmailMessage(email, rendered.Subject, rendered.Html, rendered.Text,
                EmailCategories.Account, user.SchoolId, user.Id));

        private async Task<string?> SchoolNameAsync(User user) =>
            user.SchoolId == null
                ? null
                : await _dbContext.Schools.Where(s => s.SchoolId == user.SchoolId).Select(s => s.Name).FirstOrDefaultAsync();
    }
}
