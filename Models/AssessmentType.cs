using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class AssessmentType
    {
        [Key]
        public int AssessmentTypeId { get; set; }
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;
        public string Name { get; set; } = string.Empty; // e.g. "Term Test", "PAT", "Exam"
        public float WeightPercentage { get; set; } // e.g. 25.0 for 25%
        public int Term { get; set; } // 1, 2, 3, or 4

        public ICollection<Assessment> Assessments { get; set; } = new List<Assessment>();
    }
}
