using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class WeightingStructure
    {
        [Key]
        public int WeightingStructureId { get; set; }
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;
        public int Term { get; set; } // 1-4
        public string Name { get; set; } = string.Empty; // e.g., "CAT Grade 11 Term 4"
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastModifiedDate { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;

        // Navigation
        public ICollection<WeightingNode> RootNodes { get; set; } = new List<WeightingNode>();
        public ICollection<WeightingValidation> Validations { get; set; } = new List<WeightingValidation>();
    }
}