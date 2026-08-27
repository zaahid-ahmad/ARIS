namespace ARIS1.Models
{
    public class ParentLearner
    {
        public int ParentId { get; set; }
        public Parent Parent { get; set; } = null!;

        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;

        // Optional metadata, e.g. "Mother", "Father", "Guardian" — free text, not required
        public string? Relationship { get; set; }

        // Use UtcNow, not Now — this codebase has an existing, documented issue
        // (flagged in the project's own architecture notes) where several entities
        // use DateTime.Now for CreatedDate. Do not repeat that mistake here.
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
