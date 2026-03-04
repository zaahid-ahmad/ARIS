using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class LearnerMark
    {
        [Key]
        public int MarkId { get; set; }
        public int AssessmentId { get; set; }
        public Assessment Assessment { get; set; } = null!;
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;
        public float MarksAwarded { get; set; }
        public bool IsAbsent { get; set; } = false;
    }
}
