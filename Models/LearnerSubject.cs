using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class LearnerSubject
    {
        [Key]
        public int LearnerId { get; set; }
        public Learner Learner { get; set; } = null!;
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;
        public int AcademicYear { get; set; }
    }
}
