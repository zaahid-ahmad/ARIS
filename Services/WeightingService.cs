using ARIS1.Data;
using ARIS1.Models;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public class WeightingService
    {
        private readonly AppDbContext _dbContext;

        public WeightingService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // Get or create weighting structure for a subject + term
        public async Task<WeightingStructure> GetOrCreateWeightingStructure(int subjectId, int term)
        {
            var existing = await _dbContext.WeightingStructures
                .FirstOrDefaultAsync(ws => ws.SubjectId == subjectId && ws.Term == term);

            if (existing != null) return existing;

            var subject = await _dbContext.Subjects.FindAsync(subjectId);
            if (subject == null) throw new Exception("Subject not found");

            var ws = new WeightingStructure
            {
                SubjectId = subjectId,
                Term = term,
                Name = $"{subject.Name} - Term {term}",
                Description = "",
                IsActive = true
            };

            _dbContext.WeightingStructures.Add(ws);
            await _dbContext.SaveChangesAsync();
            return ws;
        }

        // Get all assessment types for a subject
        public async Task<List<AssessmentType>> GetAssessmentTypesForTerm(int subjectId, int term)
        {
            return await _dbContext.AssessmentTypes
                .AsNoTracking()
                .Where(at => at.SubjectId == subjectId && at.Term == term)
                .ToListAsync();
        }

        // Validate weighting structure - check all nodes sum to 100% at their level
        public WeightingValidation ValidateWeightingStructure(WeightingStructure structure)
        {
            var validation = new WeightingValidation();

            if (!structure.RootNodes.Any())
            {
                validation.IsValid = false;
                validation.Message = "No weighting nodes defined.";
                return validation;
            }

            // Check root nodes sum to 100%
            var rootTotal = structure.RootNodes.Sum(n => n.Weighting);
            if (Math.Abs(rootTotal - 100m) > 0.001m) // Allow small rounding differences
            {
                validation.IsValid = false;
                validation.Message = $"Root nodes must sum to 100%. Current total: {rootTotal}%";
                return validation;
            }

            // Check each parent node's children sum to 100%
            foreach (var node in structure.RootNodes)
            {
                var nodeValidation = ValidateNodeChildren(node);
                if (!nodeValidation.IsValid)
                {
                    validation.IsValid = false;
                    validation.Message = nodeValidation.Message;
                    return validation;
                }
            }

            validation.IsValid = true;
            validation.Message = "Weighting structure is valid.";
            return validation;
        }

        private WeightingValidation ValidateNodeChildren(WeightingNode node)
        {
            var validation = new WeightingValidation();

            if (!node.ChildNodes.Any())
            {
                validation.IsValid = true;
                return validation; // Leaf node, no children to validate
            }

            var childTotal = node.ChildNodes.Sum(n => n.Weighting);
            if (Math.Abs(childTotal - 100m) > 0.001m)
            {
                validation.IsValid = false;
                validation.Message = $"Children of '{node.Name}' must sum to 100%. Current total: {childTotal}%";
                return validation;
            }

            // Recursively validate grandchildren
            foreach (var child in node.ChildNodes)
            {
                var childValidation = ValidateNodeChildren(child);
                if (!childValidation.IsValid)
                    return childValidation;
            }

            validation.IsValid = true;
            return validation;
        }

        // Create simple flat structure from assessment types
        public async Task CreateSimpleWeighting(int subjectId, int term, Dictionary<int, decimal> typeWeights)
        {
            var structure = await GetOrCreateWeightingStructure(subjectId, term);

            // Clear existing nodes
            var existingNodes = await _dbContext.WeightingNodes
                .Where(wn => wn.WeightingStructureId == structure.WeightingStructureId)
                .ToListAsync();
            _dbContext.WeightingNodes.RemoveRange(existingNodes);
            await _dbContext.SaveChangesAsync();

            // Create new nodes for each assessment type
            int order = 0;
            foreach (var kvp in typeWeights)
            {
                var type = await _dbContext.AssessmentTypes.FindAsync(kvp.Key);
                if (type == null) continue;

                var node = new WeightingNode
                {
                    WeightingStructureId = structure.WeightingStructureId,
                    ParentNodeId = null,
                    NodeType = "AssessmentType",
                    Name = type.Name,
                    Weighting = kvp.Value,
                    DisplayOrder = order++,
                    AssessmentTypeId = type.AssessmentTypeId
                };

                _dbContext.WeightingNodes.Add(node);
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}