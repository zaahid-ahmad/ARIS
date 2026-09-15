using ARIS1.Data;
using ARIS1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ARIS1.Services.Email
{
    public record EmailMessage(
        string ToAddress,
        string Subject,
        string BodyHtml,
        string BodyText,
        string Category,
        int? SchoolId = null,
        string? RecipientUserId = null,
        int? LearnerId = null,
        string? SentByUserId = null);

    // Entry point for sending email from anywhere in the app. Writes an EmailLog row per message and hands
    // it to EmailBackgroundService, so callers never wait on SMTP. Rows that can't or shouldn't be sent are
    // recorded as Skipped with the reason, so the Email Log explains why someone didn't receive an email.
    public class EmailService
    {
        private readonly AppDbContext _dbContext;
        private readonly EmailQueue _queue;
        private readonly EmailOptions _options;

        public EmailService(AppDbContext dbContext, EmailQueue queue, IOptions<EmailOptions> options)
        {
            _dbContext = dbContext;
            _queue = queue;
            _options = options.Value;
        }

        // Null when delivery is possible; otherwise why every email will be skipped.
        public string? DeliveryBlockedReason =>
            !_options.IsConfigured
                ? "Email delivery is not configured (Email:Host / Email:FromAddress)."
                : _options.RealSendBlockedReason;

        public bool IsRedirecting => !string.IsNullOrWhiteSpace(_options.RedirectAllTo);

        public async Task<EmailLog> QueueAsync(EmailMessage message) =>
            (await QueueManyAsync(new[] { message }))[0];

        public async Task<List<EmailLog>> QueueManyAsync(IEnumerable<EmailMessage> messages)
        {
            var list = messages.ToList();
            if (list.Count == 0) return new List<EmailLog>();

            // Parents who opted out of non-essential email. Account emails (password resets) always send.
            var recipientIds = list
                .Where(m => m.Category != EmailCategories.Account && m.RecipientUserId != null)
                .Select(m => m.RecipientUserId!)
                .Distinct()
                .ToList();
            var optedOut = recipientIds.Count == 0
                ? new HashSet<string>()
                : (await _dbContext.Parents
                    .Where(p => recipientIds.Contains(p.UserId) && !p.ReceiveNotificationEmails)
                    .Select(p => p.UserId)
                    .ToListAsync()).ToHashSet();

            var blocked = DeliveryBlockedReason;
            var logs = list.Select(m =>
            {
                string? skipReason =
                    string.IsNullOrWhiteSpace(m.ToAddress) ? "Recipient has no email address." :
                    m.Category != EmailCategories.Account && m.RecipientUserId != null && optedOut.Contains(m.RecipientUserId)
                        ? "Recipient has opted out of notification emails." :
                    blocked;

                return new EmailLog
                {
                    SchoolId = m.SchoolId,
                    Category = m.Category,
                    RecipientUserId = m.RecipientUserId,
                    ToAddress = m.ToAddress ?? string.Empty,
                    LearnerId = m.LearnerId,
                    Subject = Truncate(m.Subject, 300),
                    BodyHtml = m.BodyHtml,
                    BodyText = m.BodyText,
                    SentByUserId = m.SentByUserId,
                    Status = skipReason == null ? EmailStatuses.Queued : EmailStatuses.Skipped,
                    Error = skipReason,
                    CreatedUtc = DateTime.UtcNow
                };
            }).ToList();

            _dbContext.EmailLogs.AddRange(logs);
            await _dbContext.SaveChangesAsync();

            foreach (var log in logs.Where(l => l.Status == EmailStatuses.Queued))
            {
                _queue.Enqueue(log.EmailLogId);
            }

            return logs;
        }

        // Re-send from the Email Log page: Failed rows, or rows Skipped only because delivery wasn't configured at the
        // time. Never rows skipped because the parent opted out or has no address.
        public static bool CanRetry(EmailLog log) =>
            log.Status == EmailStatuses.Failed ||
            (log.Status == EmailStatuses.Skipped && log.Error != null && log.Error.StartsWith("Email"));

        public async Task<bool> RetryAsync(int emailLogId, int schoolId)
        {
            var log = await _dbContext.EmailLogs.FirstOrDefaultAsync(e => e.EmailLogId == emailLogId && e.SchoolId == schoolId);
            if (log == null || !CanRetry(log)) return false;
            if (DeliveryBlockedReason != null) return false;

            log.Status = EmailStatuses.Queued;
            log.Error = null;
            await _dbContext.SaveChangesAsync();
            _queue.Enqueue(log.EmailLogId);
            return true;
        }

        private static string Truncate(string value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
    }
}
