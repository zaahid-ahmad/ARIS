using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    // One row per email ARIS attempts to send. Written as "Queued" before sending (so pending mail is
    // visible and survives restarts), then updated by EmailBackgroundService. Doubles as the audit trail
    // behind Admin/EmailLog.razor.
    public class EmailLog
    {
        [Key]
        public int EmailLogId { get; set; }

        public int? SchoolId { get; set; }
        public School? School { get; set; }

        public string Category { get; set; } = EmailCategories.Account;

        public string? RecipientUserId { get; set; }
        public string ToAddress { get; set; } = string.Empty;
        // The address the email actually went to — differs from ToAddress when Email:RedirectAllTo is set.
        public string? DeliveredToAddress { get; set; }

        public int? LearnerId { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string BodyText { get; set; } = string.Empty;

        public string Status { get; set; } = EmailStatuses.Queued;
        public string? Error { get; set; }

        public string? SentByUserId { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? SentUtc { get; set; }
    }

    public static class EmailCategories
    {
        public const string Account = "Account";
        public const string RiskAlert = "RiskAlert";
        public const string StaffMessage = "StaffMessage";
        public const string ProgressSummary = "ProgressSummary";

        public static readonly string[] All = { Account, RiskAlert, StaffMessage, ProgressSummary };
    }

    public static class EmailStatuses
    {
        public const string Queued = "Queued";
        public const string Sent = "Sent";
        public const string Failed = "Failed";
        public const string Skipped = "Skipped";

        public static readonly string[] All = { Queued, Sent, Failed, Skipped };
    }
}
