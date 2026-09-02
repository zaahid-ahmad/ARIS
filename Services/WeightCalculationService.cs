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

        private readonly record struct AssessmentInfo(int AssessmentTypeId, string TypeName, decimal MaxMark);
        private readonly record struct LearnerMarkInfo(int AssessmentTypeId, decimal MarksAwarded, bool IsAbsent);
        private readonly record struct GradeBandInfo(decimal MinPercentage, decimal MaxPercentage, int APSLevel);

        /// <summary>
        /// Calculates the weighted term mark for a learner in a subject for a specific term.
        /// Uses mark-based logic: divides weight across total marks available for each type.
        /// </summary>
        public async Task<WeightedTermResult> CalculateWeightedTermMark(int learnerId, int subjectId, int term)
        {
            try
            {
                var weighting = await _context.WeightingStructures
                    .AsNoTracking()
                    .Include(ws => ws.RootNodes)
                    .FirstOrDefaultAsync(ws => ws.SubjectId == subjectId && ws.Term == term);
                var weightNodes = weighting?.RootNodes.ToList() ?? new List<WeightingNode>();

                var assessments = await _context.Assessments
                    .AsNoTracking()
                    .Include(a => a.AssessmentType)
                    .Where(a => a.SubjectId == subjectId && a.Term == term)
                    .Select(a => new AssessmentInfo(a.AssessmentTypeId, a.AssessmentType!.Name, a.MaxMark))
                    .ToListAsync();

                var learnerMarks = await _context.LearnerMarks
                    .AsNoTracking()
                    .Where(m => m.Assessment.SubjectId == subjectId &&
                                m.Assessment.Term == term &&
                                m.LearnerId == learnerId)
                    .Select(m => new LearnerMarkInfo(m.Assessment.AssessmentTypeId, m.MarksAwarded, m.IsAbsent))
                    .ToListAsync();

                // Unchanged from before this method was split for batching: still the existing
                // synchronous per-call GetAPSLevel (known issue #19, not addressed here).
                return BuildWeightedTermResult(weightNodes, assessments, learnerMarks,
                    pct => GetAPSLevel(subjectId, pct));
            }
            catch (Exception ex)
            {
                return new WeightedTermResult { Error = $"Error calculating weighted mark: {ex.Message}" };
            }
        }

        /// <summary>
        /// Batched version of CalculateWeightedTermMark for every learner in one subject/term —
        /// a handful of DB round trips total instead of ~3 per learner. Produces identical
        /// results to calling CalculateWeightedTermMark once per learner. Introduced for
        /// RiskAssessmentService.CalculateRiskScoresForSubject, which walks every learner in a
        /// subject and would otherwise reintroduce the exact N+1 pattern already fixed there.
        /// </summary>
        public async Task<Dictionary<int, WeightedTermResult>> CalculateWeightedTermMarksForSubject(int subjectId, int term, List<int> learnerIds)
        {
            var results = new Dictionary<int, WeightedTermResult>();
            if (learnerIds.Count == 0) return results;

            var weighting = await _context.WeightingStructures
                .AsNoTracking()
                .Include(ws => ws.RootNodes)
                .FirstOrDefaultAsync(ws => ws.SubjectId == subjectId && ws.Term == term);
            var weightNodes = weighting?.RootNodes.ToList() ?? new List<WeightingNode>();

            var assessments = await _context.Assessments
                .AsNoTracking()
                .Include(a => a.AssessmentType)
                .Where(a => a.SubjectId == subjectId && a.Term == term)
                .Select(a => new AssessmentInfo(a.AssessmentTypeId, a.AssessmentType!.Name, a.MaxMark))
                .ToListAsync();

            var marksLookup = (await _context.LearnerMarks
                    .AsNoTracking()
                    .Where(m => m.Assessment.SubjectId == subjectId && m.Assessment.Term == term && learnerIds.Contains(m.LearnerId))
                    .Select(m => new { m.LearnerId, m.Assessment.AssessmentTypeId, m.MarksAwarded, m.IsAbsent })
                    .ToListAsync())
                .GroupBy(m => m.LearnerId)
                .ToDictionary(g => g.Key, g => g.Select(x => new LearnerMarkInfo(x.AssessmentTypeId, x.MarksAwarded, x.IsAbsent)).ToList());

            // Grade bands loaded once and looked up in memory below, rather than one blocking
            // query per learner (what GetAPSLevel would otherwise do inside this loop).
            var gradeBands = await _context.GradeBands
                .AsNoTracking()
                .Where(gb => gb.SubjectId == subjectId)
                .Select(gb => new GradeBandInfo(gb.MinPercentage, gb.MaxPercentage, gb.APSLevel))
                .ToListAsync();

            foreach (var learnerId in learnerIds)
            {
                var learnerMarks = marksLookup.GetValueOrDefault(learnerId, new List<LearnerMarkInfo>());
                results[learnerId] = BuildWeightedTermResult(weightNodes, assessments, learnerMarks,
                    pct => GetApsLevelFromBands(gradeBands, pct));
            }

            return results;
        }

        // Single source of truth for the per-type weighted-sum math — shared by the one-learner
        // and batched paths above so they can never drift apart.
        private static WeightedTermResult BuildWeightedTermResult(
            List<WeightingNode> weightNodes,
            List<AssessmentInfo> assessments,
            List<LearnerMarkInfo> learnerMarks,
            Func<decimal, int> getApsLevel)
        {
            var result = new WeightedTermResult();

            if (weightNodes.Count == 0)
            {
                result.Error = "No weighting structure configured for this term.";
                return result;
            }

            if (assessments.Count == 0)
            {
                result.Error = "No assessments found for this term.";
                return result;
            }

            var typeGroups = assessments.GroupBy(a => a.AssessmentTypeId).ToList();

            decimal weightedTotal = 0m;
            decimal totalWeight = 0m;
            bool hasMarks = false;

            foreach (var typeGroup in typeGroups)
            {
                var assessmentTypeId = typeGroup.Key;
                var typeName = typeGroup.First().TypeName;

                var weightNode = weightNodes.FirstOrDefault(n => n.AssessmentTypeId == assessmentTypeId);
                if (weightNode == null)
                    continue;

                decimal weight = weightNode.Weighting;
                decimal totalMarksForType = typeGroup.Sum(a => a.MaxMark);

                var marksForType = learnerMarks
                    .Where(m => m.AssessmentTypeId == assessmentTypeId)
                    .Sum(m => m.IsAbsent ? 0m : m.MarksAwarded);

                decimal percentageForType = totalMarksForType > 0
                    ? (marksForType / totalMarksForType) * 100m
                    : 0m;

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

            if (Math.Abs(totalWeight - 100m) > 0.001m)
            {
                result.Error = $"Weights do not sum to 100% (Total: {totalWeight}%). Please fix the weighting structure.";
                return result;
            }

            result.WeightedPercentage = hasMarks ? weightedTotal : 0m;
            result.APSLevel = getApsLevel(result.WeightedPercentage);
            result.IsSuccessful = true;
            return result;
        }

        private static int GetApsLevelFromBands(List<GradeBandInfo> bands, decimal percentage)
        {
            foreach (var band in bands)
            {
                if (band.MinPercentage <= percentage && band.MaxPercentage >= percentage)
                    return band.APSLevel;
            }

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
        /// Calculates a learner's year (promotion) mark for a subject from its Year-level
        /// weighting structure (WeightingStructure.Term == 0). Built entirely out of calls to
        /// CalculateWeightedTermMark — a "Term" node substitutes in that term's weighted mark,
        /// an "AssessmentType" node substitutes in that single type's percentage (e.g. a Final
        /// Exam), and "Custom" group nodes just recurse into their children. Node weightings are
        /// relative to their parent, same convention as WeightingService's tree validation.
        /// </summary>
        public async Task<YearMarkResult> CalculateYearMark(int learnerId, int subjectId)
        {
            var result = new YearMarkResult();

            try
            {
                var structure = await _context.WeightingStructures
                    .AsNoTracking()
                    .Include(ws => ws.RootNodes)
                    .ThenInclude(n => n.ChildNodes)
                    .FirstOrDefaultAsync(ws => ws.SubjectId == subjectId && ws.Term == 0);

                // WeightingStructure.RootNodes is actually every node belonging to the structure
                // (the collection side of the WeightingStructureId FK), not a true-roots-only
                // filter — filter down to top-level nodes explicitly. Each node's own ChildNodes
                // (populated above via ThenInclude, keyed off ParentNodeId) is unaffected.
                var rootNodes = structure?.RootNodes.Where(n => n.ParentNodeId == null).ToList() ?? new List<WeightingNode>();

                if (structure == null || rootNodes.Count == 0)
                {
                    result.Error = "No year weighting structure configured for this subject.";
                    return result;
                }

                decimal rootTotal = rootNodes.Sum(n => n.Weighting);
                if (Math.Abs(rootTotal - 100m) > 0.001m)
                {
                    result.Error = $"Year weighting nodes do not sum to 100% (Total: {rootTotal}%). Please fix the year weighting structure.";
                    return result;
                }

                decimal yearPercentage = 0m;
                bool hasAnyData = false;

                foreach (var node in rootNodes.OrderBy(n => n.DisplayOrder))
                {
                    var (contribution, hasData) = await EvaluateYearNode(learnerId, subjectId, node, node.Weighting);
                    yearPercentage += contribution;
                    hasAnyData |= hasData;

                    result.Breakdown.Add(new YearMarkBreakdown
                    {
                        NodeName = node.Name,
                        Weight = node.Weighting,
                        ContributionToYear = contribution
                    });
                }

                result.YearPercentage = hasAnyData ? yearPercentage : 0m;
                result.APSLevel = GetAPSLevel(subjectId, result.YearPercentage);
                result.IsSuccessful = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"Error calculating year mark: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Recursively evaluates one Year-structure node's contribution to the year percentage.
        /// parentScalePercent is this node's own weighting already multiplied down from every
        /// ancestor (e.g. a Term node at 30% under an SBA group at 25% contributes at 7.5%).
        /// </summary>
        private async Task<(decimal Contribution, bool HasData)> EvaluateYearNode(
            int learnerId, int subjectId, WeightingNode node, decimal parentScalePercent)
        {
            if (node.ChildNodes.Count > 0)
            {
                decimal total = 0m;
                bool anyData = false;
                foreach (var child in node.ChildNodes.OrderBy(c => c.DisplayOrder))
                {
                    var childScale = parentScalePercent * (child.Weighting / 100m);
                    var (contribution, hasData) = await EvaluateYearNode(learnerId, subjectId, child, childScale);
                    total += contribution;
                    anyData |= hasData;
                }
                return (total, anyData);
            }

            if (node.NodeType == "Term" && node.ReferencedTerm.HasValue)
            {
                var termResult = await CalculateWeightedTermMark(learnerId, subjectId, node.ReferencedTerm.Value);
                if (!termResult.IsSuccessful) return (0m, false);
                return (parentScalePercent * (termResult.WeightedPercentage / 100m), true);
            }

            if (node.NodeType == "AssessmentType" && node.AssessmentTypeId.HasValue)
            {
                var typePercentage = await CalculateAssessmentTypePercentage(learnerId, node.AssessmentTypeId.Value);
                if (!typePercentage.HasValue) return (0m, false);
                return (parentScalePercent * (typePercentage.Value / 100m), true);
            }

            return (0m, false);
        }

        /// <summary>
        /// A learner's percentage for a single AssessmentType across all its Assessments,
        /// independent of any term's weighting structure — used for Year-level "AssessmentType"
        /// nodes (e.g. a Final Exam) that stand on their own rather than being blended into a
        /// term's overall weighted mark.
        /// </summary>
        private async Task<decimal?> CalculateAssessmentTypePercentage(int learnerId, int assessmentTypeId)
        {
            var assessments = await _context.Assessments
                .Where(a => a.AssessmentTypeId == assessmentTypeId)
                .ToListAsync();

            decimal totalMarksAvailable = assessments.Sum(a => a.MaxMark);
            if (totalMarksAvailable <= 0) return null;

            var assessmentIds = assessments.Select(a => a.AssessmentId).ToList();
            var learnerMarks = await _context.LearnerMarks
                .Where(m => assessmentIds.Contains(m.AssessmentId) && m.LearnerId == learnerId)
                .ToListAsync();

            decimal marksEarned = learnerMarks.Sum(m => m.IsAbsent ? 0m : m.MarksAwarded);
            return (marksEarned / totalMarksAvailable) * 100m;
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
    /// Result of a year (promotion) mark calculation from a subject's Year-level weighting structure
    /// </summary>
    public class YearMarkResult
    {
        public decimal YearPercentage { get; set; }
        public int APSLevel { get; set; }
        public bool IsSuccessful { get; set; }
        public string? Error { get; set; }
        public List<YearMarkBreakdown> Breakdown { get; set; } = new();

        public string GetGrade() => WeightCalculationService.GetGradeLetterFromAPS(APSLevel);
    }

    /// <summary>
    /// Breakdown of how each root node of a Year weighting structure contributes to the year mark
    /// </summary>
    public class YearMarkBreakdown
    {
        public string NodeName { get; set; } = string.Empty;
        public decimal Weight { get; set; }
        public decimal ContributionToYear { get; set; }
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