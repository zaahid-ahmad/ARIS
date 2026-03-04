using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Teacher
    {
        [Key]
        public int TeacherId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
    }
}
