using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    // A frozen snapshot of a learner's risk score for one subject, taken at the moment that
    // subject's academic year is closed out by a Year Rollover. Subject is itself year-scoped
    // (a distinct row per grade per year), so one row here naturally corresponds to exactly one
    // learner + one subject-instance + the year it belonged to.
    public class LearnerYearSubjectRisk
    {
        [Key]
        public int LearnerYearSubjectRiskId { get; set; }
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;
        public int AcademicYear { get; set; }
        public decimal Score { get; set; }
        public string Level { get; set; } = string.Empty; // Critical, High, Moderate, Low
        public decimal AcademicAverage { get; set; }
        public decimal AttendancePercentage { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
