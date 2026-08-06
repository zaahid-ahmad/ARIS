using ARIS1.Models;
using ARIS1.Data;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public class WeightCalculationService
    {
        private readonly AppDbContext _context;

        public WeightCalculationService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Calculates the weighted term mark for a learner in a subject for a specific term.
        /// Uses mark-based logic: divides weight across total marks available for each type.
        /// </summary>
        public async Task<WeightedTermResult> CalculateWeightedTermMark(int learnerId, int subjectId, int term)
        {
            var result = new WeightedTermResult();

            try
            {
                // Get the weighting structure for this subject/term
                var weighting = await _context.WeightingStructures
                    .Include(ws => ws.RootNodes)
                    .FirstOrDefaultAsync(ws => ws.SubjectId == subjectId && ws.Term == term);

                if (weighting == null || weighting.RootNodes.Count == 0)
                {
                    result.Error = "No weighting structure configured for this term.";
                    return result;
                }

                // Get all assessments for this subject/term
                var assessments = await _context.Assessments
                    .Include(a => a.AssessmentType)
                    .Where(a => a.SubjectId == subjectId && a.Term == term)
                    .ToListAsync();

                if (assessments.Count == 0)
                {
                    result.Error = "No assessments found for this term.";
                    return result;
                }

                // Get all learner marks for these assessments
                var learnerMarks = await _context.LearnerMarks
                    .Where(m => m.Assessment.SubjectId == subjectId &&
                                m.Assessment.Term == term &&
                                m.LearnerId == learnerId)
                    .Include(m => m.Assessment)
                    .ToListAsync();

                // Group assessments by AssessmentTypeId to calculate totals
                var typeGroups = assessments.GroupBy(a => a.AssessmentTypeId).ToList();

                decimal weightedTotal = 0m;
                decimal totalWeight = 0m;
                bool hasMarks = false;

                foreach (var typeGroup in typeGroups)
                {
                    var assessmentTypeId = typeGroup.Key;
                    var typeName = typeGroup.First().AssessmentType?.Name ?? "Unknown";

                    // Get the weight for this assessment type
                    var weightNode = weighting.RootNodes.FirstOrDefault(n => n.AssessmentTypeId == assessmentTypeId);
                    if (weightNode == null)
                        continue;

                    decimal weight = weightNode.Weighting;

                    // Calculate total marks available for this assessment type
                    decimal totalMarksForType = typeGroup.Sum(a => a.MaxMark);

                    // Get learner's total marks for this assessment type
                    var marksForType = learnerMarks
                        .Where(m => m.Assessment.AssessmentTypeId == assessmentTypeId)
                        .Sum(m => m.IsAbsent ? 0m : m.MarksAwarded);

                    // Calculate percentage for this type
                    decimal percentageForType = totalMarksForType > 0
                        ? (marksForType / totalMarksForType) * 100m
                        : 0m;

                    // Add to weighted total: (percentage × weight)
                    decimal contributionToTerm = (percentageForType * weight) / 100m;
                    weightedTotal += contributionToTerm;
                    totalWeight += weight;

                    result.TypeBreakdown.Add(new AssessmentTypeBreakdown
                    {
                        AssessmentTypeName = typeName,
                        Weight = weight,
                        TotalMarksAvailable = totalMarksForType,
                        LearnerMarksEarned = marksForType,
                        PercentageForType = percentageForType,
                        ContributionToTerm = contributionToTerm
                    });

                    if (marksForType > 0)
                        hasMarks = true;
                }

                // Validate that weights sum to 100%
                if (Math.Abs(totalWeight - 100m) > 0.001m)
                {
                    result.Error = $"Weights do not sum to 100% (Total: {totalWeight}%). Please fix the weighting structure.";
                    return result;
                }

                // Set results
                result.WeightedPercentage = hasMarks ? weightedTotal : 0m;
                result.APSLevel = GetAPSLevel(subjectId, result.WeightedPercentage);
                result.IsSuccessful = true;

                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"Error calculating weighted mark: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Gets the APS level (0-7) based on percentage using the GradeBand configuration.
        /// Falls back to default bands if custom bands not configured.
        /// </summary>
        private int GetAPSLevel(int subjectId, decimal percentage)
        {
            // Try to find custom grade bands for this subject
            var gradeBand = _context.GradeBands
                .AsNoTracking()
                .FirstOrDefault(gb => gb.SubjectId == subjectId &&
                                      gb.MinPercentage <= percentage &&
                                      gb.MaxPercentage >= percentage);

            if (gradeBand != null)
                return gradeBand.APSLevel;

            // Fall back to default APS bands
            return percentage switch
            {
                >= 80m => 7,
                >= 70m => 6,
                >= 60m => 5,
                >= 50m => 4,
                >= 40m => 3,
                >= 30m => 2,
                _ => 0
            };
        }

        /// <summary>
        /// Gets the letter grade based on APS level.
        /// </summary>
        public static string GetGradeLetterFromAPS(int apsLevel)
        {
            return apsLevel switch
            {
                7 => "A",
                6 => "B",
                5 => "C",
                4 => "D",
                3 => "E",
                2 => "F",
                0 => "F",
                _ => "N/A"
            };
        }

        /// <summary>
        /// Validates that the total marks for all assessments of a type don't exceed reasonable limits.
        /// </summary>
        public async Task<ValidationResult> ValidateAssessmentMarksForType(int assessmentTypeId, int subjectId, int term)
        {
            var result = new ValidationResult { IsValid = true };

            try
            {
                var weighting = await _context.WeightingStructures
                    .Include(ws => ws.RootNodes)
                    .FirstOrDefaultAsync(ws => ws.SubjectId == subjectId && ws.Term == term);

                if (weighting == null)
                {
                    result.IsValid = false;
                    result.Message = "No weighting structure found for this term.";
                    return result;
                }

                var weightNode = weighting.RootNodes.FirstOrDefault(n => n.AssessmentTypeId == assessmentTypeId);
                if (weightNode == null)
                {
                    result.IsValid = false;
                    result.Message = "Assessment type not in weighting structure.";
                    return result;
                }

                var assessments = await _context.Assessments
                    .Where(a => a.AssessmentTypeId == assessmentTypeId && a.Term == term)
                    .ToListAsync();

                decimal totalMarks = assessments.Sum(a => a.MaxMark);

                // Reasonable limit: allow up to 150% of what the weight suggests
                // (weight 50% could have up to 75 marks out of 100 total)
                decimal reasonableLimit = (weightNode.Weighting / 100m) * 150m;

                if (totalMarks > reasonableLimit)
                {
                    result.IsValid = false;
                    result.Message = $"Total marks for this assessment type ({totalMarks}) exceeds reasonable limit ({reasonableLimit:F1}) based on {weightNode.Weighting}% weight. Consider spreading across multiple terms or reducing assessment scope.";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Message = $"Validation error: {ex.Message}";
                return result;
            }
        }
    }

    /// <summary>
    /// Result of weighted term mark calculation
    /// </summary>
    public class WeightedTermResult
    {
        public decimal WeightedPercentage { get; set; }
        public int APSLevel { get; set; }
        public bool IsSuccessful { get; set; }
        public string? Error { get; set; }
        public List<AssessmentTypeBreakdown> TypeBreakdown { get; set; } = new();

        public string GetGrade() => WeightCalculationService.GetGradeLetterFromAPS(APSLevel);
    }

    /// <summary>
    /// Breakdown of how each assessment type contributes to the final term mark
    /// </summary>
    public class AssessmentTypeBreakdown
    {
        public string AssessmentTypeName { get; set; } = string.Empty;
        public decimal Weight { get; set; }
        public decimal TotalMarksAvailable { get; set; }
        public decimal LearnerMarksEarned { get; set; }
        public decimal PercentageForType { get; set; }
        public decimal ContributionToTerm { get; set; }
    }

    /// <summary>
    /// Validation result for assessment marks
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}