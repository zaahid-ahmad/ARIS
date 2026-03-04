using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class AttendanceSession
    {
        [Key]
        public int SessionId { get; set; }
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;
        public DateTime Date { get; set; }
        public int TeacherId { get; set; }
        public Teacher Teacher { get; set; } = null!;
        public string? Notes { get; set; }

        public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    }
}
