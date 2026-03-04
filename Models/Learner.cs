using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Learner
    {
        [Key]
        public int LearnerId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;
        public int Grade { get; set; } // 10, 11, or 12
        public string ClassName { get; set; } = string.Empty; // e.g. "10A"
        public int EnrollmentYear { get; set; }

        public ICollection<LearnerSubject> LearnerSubjects { get; set; } = new List<LearnerSubject>();
        public ICollection<LearnerMark> LearnerMarks { get; set; } = new List<LearnerMark>();
        public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    }
}
