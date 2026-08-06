using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class LearnerQuestionMark
    {
        [Key]
        public int QuestionMarkId { get; set; }
        public int QuestionId { get; set; }
        public AssessmentQuestion Question { get; set; } = null!;
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;
        public decimal MarksAwarded { get; set; }
    }
}