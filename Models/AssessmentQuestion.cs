using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class AssessmentQuestion
    {
        [Key]
        public int QuestionId { get; set; }
        public int AssessmentId { get; set; }
        public Assessment Assessment { get; set; } = null!;
        public int QuestionNumber { get; set; }
        public string Topic { get; set; } = string.Empty;
        public float MaxMark { get; set; }

        public ICollection<LearnerQuestionMark> LearnerMarks { get; set; } = new List<LearnerQuestionMark>();
    }
}