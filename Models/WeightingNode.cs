using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class WeightingNode
    {
        [Key]
        public int WeightingNodeId { get; set; }
        public int WeightingStructureId { get; set; }
        public WeightingStructure WeightingStructure { get; set; } = null!;

        // Self-referencing for hierarchy
        public int? ParentNodeId { get; set; }
        public WeightingNode? ParentNode { get; set; }

        // Node metadata
        public string NodeType { get; set; } = string.Empty;
        // "AssessmentType", "Assessment", "Custom", "Task"

        public string Name { get; set; } = string.Empty; // e.g., "SBA", "Task 1", "Paper 1"
        public decimal Weighting { get; set; } // percentage at this level (0-100)
        public int DisplayOrder { get; set; }

        // Optional link to actual AssessmentType (if NodeType == "AssessmentType")
        public int? AssessmentTypeId { get; set; }
        public AssessmentType? AssessmentType { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation to children
        public ICollection<WeightingNode> ChildNodes { get; set; } = new List<WeightingNode>();
    }
}