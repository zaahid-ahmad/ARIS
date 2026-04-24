using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class WeightingValidation
    {
        [Key]
        public int WeightingValidationId { get; set; }
        public int WeightingStructureId { get; set; }
        public WeightingStructure WeightingStructure { get; set; } = null!;
        public string NodePath { get; set; } = string.Empty; // e.g., "SBA/Task3/Paper1"
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime CheckedDate { get; set; } = DateTime.Now;
    }
}