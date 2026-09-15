namespace ARIS1.Models
{
    // Last risk level a learner's parents were emailed about for one subject. ParentAlertService only
    // emails again when the level gets worse, so parents aren't re-alerted every time marks are captured.
    public class ParentAlertState
    {
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;

        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public string LastAlertLevel { get; set; } = string.Empty; // "High" or "Critical"
        public DateTime LastAlertedUtc { get; set; } = DateTime.UtcNow;
    }
}
