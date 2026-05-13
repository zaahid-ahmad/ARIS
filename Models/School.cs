namespace ARIS1.Models
{
    public class School
    {
        public int SchoolId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime LastModifiedDate { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        // Navigation
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
    }
}