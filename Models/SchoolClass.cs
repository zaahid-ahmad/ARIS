namespace ARIS1.Models
{
    public class SchoolClass
    {
        public int ClassId { get; set; }
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;
        public int Grade { get; set; } // 10, 11, or 12
        public string Name { get; set; } = string.Empty; // e.g. "A"

        public ICollection<Learner> Learners { get; set; } = new List<Learner>();
    }
}
