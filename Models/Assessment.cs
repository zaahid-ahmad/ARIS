using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Assessment
    {
        [Key]
        public int AssessmentId { get; set; }
        public int AssessmentTypeId { get; set; }
        public AssessmentType AssessmentType { get; set; } = null!;
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;
        public string Title { get; set; } = string.Empty;
        public decimal MaxMark { get; set; }
        public DateTime Date { get; set; }
        public int Term { get; set; }

        public ICollection<LearnerMark> LearnerMarks { get; set; } = new List<LearnerMark>();
}
}
