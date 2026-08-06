using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Intervention
    {
        [Key]
        public int InterventionId { get; set; }
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;
        public int QuestionId { get; set; }
        public AssessmentQuestion Question { get; set; } = null!;
        public string Topic { get; set; } = string.Empty;
        public decimal PercentageScore { get; set; }
        public string Level { get; set; } = string.Empty; // Critical, Attention, Focus, Minor, WellDone
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public bool IsResolved { get; set; } = false;
    }
}