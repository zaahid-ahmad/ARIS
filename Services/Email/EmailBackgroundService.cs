using ARIS1.Data;
using ARIS1.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ARIS1.Services.Email
{
    // Sends queued emails one at a time over SMTP (MailKit), throttled by Email:SendsPerMinute, and records
    // the outcome on each EmailLog row.
    public class EmailBackgroundService : BackgroundService
    {
        private readonly EmailQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly EmailOptions _options;
        private readonly ILogger<EmailBackgroundService> _logger;

        public EmailBackgroundService(EmailQueue queue, IServiceScopeFactory scopeFactory,
            IOptions<EmailOptions> options, ILogger<EmailBackgroundService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RequeuePendingAsync(stoppingToken);

            var delay = TimeSpan.FromMilliseconds(60_000.0 / Math.Max(1, _options.SendsPerMinute));

            await foreach (var emailLogId in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendOneAsync(emailLogId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error processing email {EmailLogId}.", emailLogId);
                }

                await Task.Delay(delay, stoppingToken);
            }
        }

        private async Task RequeuePendingAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var pending = await db.EmailLogs
                    .Where(e => e.Status == EmailStatuses.Queued)
                    .OrderBy(e => e.EmailLogId)
                    .Select(e => e.EmailLogId)
                    .ToListAsync(stoppingToken);

                foreach (var id in pending) _queue.Enqueue(id);
                if (pending.Count > 0)
                    _logger.LogInformation("Re-queued {Count} pending email(s) from a previous run.", pending.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not re-queue pending emails on startup.");
            }
        }

        private async Task SendOneAsync(int emailLogId, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var log = await db.EmailLogs.FirstOrDefaultAsync(e => e.EmailLogId == emailLogId, stoppingToken);
            if (log == null || log.Status != EmailStatuses.Queued) return;

            // Final gate, re-checked at send time with the current configuration: rows queued earlier (and
            // re-queued after a restart) must not reach real recipients if redirect has since been removed.
            var blocked = !_options.IsConfigured
                ? "Email delivery is not configured (Email:Host / Email:FromAddress)."
                : _options.RealSendBlockedReason;
            if (blocked != null)
            {
                log.Status = EmailStatuses.Skipped;
                log.Error = blocked;
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            var redirect = string.IsNullOrWhiteSpace(_options.RedirectAllTo) ? null : _options.RedirectAllTo.Trim();
            var deliverTo = redirect ?? log.ToAddress;

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
                message.To.Add(MailboxAddress.Parse(deliverTo));
                message.Subject = redirect == null ? log.Subject : $"[To: {log.ToAddress}] {log.Subject}";
                message.Body = new BodyBuilder { HtmlBody = log.BodyHtml, TextBody = log.BodyText }.ToMessageBody();

                using var client = new SmtpClient { Timeout = 30_000 };
                await client.ConnectAsync(_options.Host, _options.Port,
                    _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, stoppingToken);
                if (!string.IsNullOrWhiteSpace(_options.Username))
                {
                    await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, stoppingToken);
                }
                await client.SendAsync(message, stoppingToken);
                await client.DisconnectAsync(true, stoppingToken);

                log.Status = EmailStatuses.Sent;
                log.DeliveredToAddress = deliverTo;
                log.SentUtc = DateTime.UtcNow;
                log.Error = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to send email {EmailLogId} to {To}.", log.EmailLogId, deliverTo);
                log.Status = EmailStatuses.Failed;
                log.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
