using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class GradeBand
    {
        [Key]
        public int GradeBandId { get; set; }
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public decimal MinPercentage { get; set; } // e.g., 80
        public decimal MaxPercentage { get; set; } // e.g., 100
        public int APSLevel { get; set; } // 7, 6, 5, 4, 3, 2, 0
        public string Grade { get; set; } = string.Empty; // Optional: "A", "B", "C", etc.

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}