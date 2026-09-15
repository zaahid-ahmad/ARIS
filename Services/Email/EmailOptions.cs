namespace ARIS1.Services.Email
{
    // Bound to the "Email" configuration section. Secrets (Username/Password) belong in user-secrets:
    //   dotnet user-secrets set "Email:Host" "smtp-relay.brevo.com"
    //   dotnet user-secrets set "Email:Password" "<smtp key>"
    public class EmailOptions
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public bool UseStartTls { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
        public string FromName { get; set; } = "ARIS";

        // When set, every email is delivered to this address instead of the real recipient (the original
        // recipient is shown in the subject). Seeded demo parents have realistic public addresses that real
        // people may own, so this should stay set whenever demo data is in use.
        public string? RedirectAllTo { get; set; }

        // Must be explicitly true to email real recipients (i.e. to send with RedirectAllTo empty). Enforced in
        // every environment, both when emails are queued and again at the final send step.
        public bool AllowRealRecipients { get; set; } = false;

        public string? RealSendBlockedReason =>
            string.IsNullOrWhiteSpace(RedirectAllTo) && !AllowRealRecipients
                ? "Email:RedirectAllTo is empty and Email:AllowRealRecipients is not true, so real recipients can't be emailed."
                : null;

        // Throttle to stay within free-tier provider limits (e.g. Brevo 300/day, Gmail ~500/day).
        public int SendsPerMinute { get; set; } = 20;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
    }
}
