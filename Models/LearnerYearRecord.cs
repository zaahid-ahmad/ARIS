using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class LearnerYearRecord
    {
        [Key]
        public int LearnerYearRecordId { get; set; }
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;
        public int AcademicYear { get; set; }
        public int Grade { get; set; } // the grade the learner was in during AcademicYear
        public int ClassId { get; set; }
        public SchoolClass Class { get; set; } = null!;
        public string Outcome { get; set; } = string.Empty; // "Promoted", "Repeated", "Graduated", "Withdrawn"
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
