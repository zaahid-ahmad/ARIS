using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Subject
    {
        [Key]
        public int SubjectId { get; set; }
        public string Name { get; set; } = string.Empty; // e.g. "CAT Grade 10"
        public int Grade { get; set; }
        public int TeacherId { get; set; }
        public Teacher Teacher { get; set; } = null!;
        public int AcademicYear { get; set; }

        public ICollection<LearnerSubject> LearnerSubjects { get; set; } = new List<LearnerSubject>();
        public ICollection<Assessment> Assessments { get; set; } = new List<Assessment>();
        public ICollection<AttendanceSession> AttendanceSessions { get; set; } = new List<AttendanceSession>();
    }
}
