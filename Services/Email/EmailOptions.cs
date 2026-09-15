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
        // recipient is shown in the subject). Required for real sending in Development, since seeded
        // accounts use fake addresses.
        public string? RedirectAllTo { get; set; }

        // Throttle to stay within free-tier provider limits (e.g. Brevo 300/day, Gmail ~500/day).
        public int SendsPerMinute { get; set; } = 20;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
    }
}
