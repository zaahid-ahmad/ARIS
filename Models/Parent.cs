using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Parent
    {
        [Key]
        public int ParentId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        // Opt-out for non-essential email (risk alerts, staff messages, progress summaries).
        // Account emails such as password resets are always sent.
        public bool ReceiveNotificationEmails { get; set; } = true;

        public ICollection<ParentLearner> Children { get; set; } = new List<ParentLearner>();
    }
}
