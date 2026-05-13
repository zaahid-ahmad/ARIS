using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Subject
    {
        [Key]
        public int SubjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Grade { get; set; }
        public int TeacherId { get; set; }
        public Teacher Teacher { get; set; } = null!;
        public int AcademicYear { get; set; }

        // School FK
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public ICollection<LearnerSubject> LearnerSubjects { get; set; } = new List<LearnerSubject>();
        public ICollection<Assessment> Assessments { get; set; } = new List<Assessment>();
        public ICollection<AttendanceSession> AttendanceSessions { get; set; } = new List<AttendanceSession>();
        public ICollection<WeightingStructure> WeightingStructures { get; set; } = new List<WeightingStructure>();
        public ICollection<GradeBand> GradeBands { get; set; } = new List<GradeBand>();
    }
}