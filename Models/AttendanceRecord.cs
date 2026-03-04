using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class AttendanceRecord
    {
        [Key] 
        public int RecordId { get; set; }
        public int SessionId { get; set; }
        public AttendanceSession Session { get; set; } = null!;
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;
        public string Status { get; set; } = string.Empty; // "Present", "Absent", "Late", "Excused"
    }
}
