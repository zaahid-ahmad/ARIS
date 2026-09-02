using ARIS1.Data;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public class RiskData
    {
        public int LearnerId { get; set; }
        public int SubjectId { get; set; }
        public string Level { get; set; } = string.Empty;
        public decimal Score { get; set; }
        public decimal AttendancePercentage { get; set; }
        public decimal AcademicAverage { get; set; }
        public string Intervention { get; set; } = string.Empty;
    }

    public class RiskAssessmentService
    {
        public const decimal AtRiskThreshold = 45m;

        private readonly AppDbContext _dbContext;
        private readonly WeightCalculationService _weightCalculationService;

        public RiskAssessmentService(AppDbContext dbContext, WeightCalculationService weightCalculationService)
        {
            _dbContext = dbContext;
            _weightCalculationService = weightCalculationService;
        }

        public async Task<RiskData> CalculateRiskScore(int learnerId, int subjectId)
        {
            var attendanceStatuses = await _dbContext.AttendanceRecords
                .AsNoTracking()
                .Where(ar => ar.LearnerId == learnerId)
                .Select(ar => ar.Status)
                .ToListAsync();

            // "Current term" is resolved per-learner (their own most recent term with a recorded
            // mark), not subject-wide — a subject-wide MAX(Assessment.Term) would make every
            // learner who hasn't been marked yet in a brand-new term look like they have a 0%
            // academic average the moment ANY assessment exists for that term, even before the
            // rest of the class has been captured for it.
            var currentTerm = await _dbContext.LearnerMarks
                .AsNoTracking()
                .Where(m => m.Assessment.SubjectId == subjectId && m.LearnerId == learnerId && !m.IsAbsent)
                .Select(m => (int?)m.Assessment.Term)
                .MaxAsync();

            decimal academicAverage = 0m;
            if (currentTerm.HasValue)
            {
                var currentTermMarks = await _dbContext.LearnerMarks
                    .AsNoTracking()
                    .Where(m => m.Assessment.SubjectId == subjectId && m.Assessment.Term == currentTerm.Value
                                && m.LearnerId == learnerId && !m.IsAbsent)
                    .Select(m => new MarkInfo(m.MarksAwarded, m.Assessment.MaxMark))
                    .ToListAsync();

                var weightedResult = await _weightCalculationService.CalculateWeightedTermMark(learnerId, subjectId, currentTerm.Value);
                academicAverage = ResolveAcademicAverage(weightedResult, currentTermMarks);
            }

            return BuildRiskData(learnerId, subjectId, attendanceStatuses, academicAverage);
        }

        /// <summary>
        /// Batched version of CalculateRiskScore for every learner in one subject — a handful of
        /// DB round trips per distinct "current term" among the group instead of several per
        /// learner. Produces identical results to calling CalculateRiskScore once per learner;
        /// introduced because callers that walk every learner in a subject
        /// (Teacher/Dashboard.razor, Teacher/AtRisk.razor) were making hundreds of sequential
        /// round trips for subjects with many enrolled learners.
        /// </summary>
        public async Task<Dictionary<int, RiskData>> CalculateRiskScoresForSubject(int subjectId, List<int> learnerIds)
        {
            var results = new Dictionary<int, RiskData>();
            if (learnerIds.Count == 0) return results;

            var attendanceLookup = (await _dbContext.AttendanceRecords
                    .AsNoTracking()
                    .Where(ar => learnerIds.Contains(ar.LearnerId))
                    .Select(ar => new { ar.LearnerId, ar.Status })
                    .ToListAsync())
                .GroupBy(ar => ar.LearnerId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Status).ToList());

            // Each learner's own "current term" — see the single-learner method's comment for why
            // this must be per-learner rather than one subject-wide MAX(Assessment.Term).
            var learnerCurrentTerms = (await _dbContext.LearnerMarks
                    .AsNoTracking()
                    .Where(m => m.Assessment.SubjectId == subjectId && learnerIds.Contains(m.LearnerId) && !m.IsAbsent)
                    .Select(m => new { m.LearnerId, m.Assessment.Term })
                    .ToListAsync())
                .GroupBy(m => m.LearnerId)
                .ToDictionary(g => g.Key, g => g.Max(x => x.Term));

            var academicAverages = new Dictionary<int, decimal>();

            // Group learners by their shared current term so each distinct term is only queried
            // once — typically 1-2 distinct terms in practice (most of a class is marked up to
            // the same point at the same time), never one query per learner.
            foreach (var group in learnerIds.Where(learnerCurrentTerms.ContainsKey).GroupBy(id => learnerCurrentTerms[id]))
            {
                int term = group.Key;
                var idsForTerm = group.ToList();

                var marksLookup = (await _dbContext.LearnerMarks
                        .AsNoTracking()
                        .Where(m => m.Assessment.SubjectId == subjectId && m.Assessment.Term == term
                                    && idsForTerm.Contains(m.LearnerId) && !m.IsAbsent)
                        .Select(m => new { m.LearnerId, m.MarksAwarded, MaxMark = m.Assessment.MaxMark })
                        .ToListAsync())
                    .GroupBy(m => m.LearnerId)
                    .ToDictionary(g => g.Key, g => g.Select(x => new MarkInfo(x.MarksAwarded, x.MaxMark)).ToList());

                var weightedLookup = await _weightCalculationService.CalculateWeightedTermMarksForSubject(subjectId, term, idsForTerm);

                foreach (var learnerId in idsForTerm)
                {
                    var weightedResult = weightedLookup.GetValueOrDefault(learnerId) ?? new WeightedTermResult();
                    academicAverages[learnerId] = ResolveAcademicAverage(weightedResult, marksLookup.GetValueOrDefault(learnerId, new List<MarkInfo>()));
                }
            }

            foreach (var learnerId in learnerIds)
            {
                results[learnerId] = BuildRiskData(
                    learnerId, subjectId,
                    attendanceLookup.GetValueOrDefault(learnerId, new List<string>()),
                    academicAverages.GetValueOrDefault(learnerId, 0m));
            }

            return results;
        }

        /// <summary>
        /// Academic input for the risk score: the learner's properly type-weighted mark for their
        /// own current term when a WeightingStructure is configured there; otherwise a simple
        /// average of just that term's marks (never blended across the subject's whole history,
        /// unlike the old flat all-time average) so scoring keeps working before an admin has set
        /// weighting up for a term.
        /// </summary>
        private static decimal ResolveAcademicAverage(WeightedTermResult weightedResult, List<MarkInfo> currentTermMarks)
        {
            if (weightedResult.IsSuccessful)
                return weightedResult.WeightedPercentage;

            if (currentTermMarks.Count == 0)
                return 0m;

            decimal totalPercentage = 0m;
            foreach (var mark in currentTermMarks)
                totalPercentage += (mark.MarksAwarded / mark.MaxMark) * 100m;
            return totalPercentage / currentTermMarks.Count;
        }

        private readonly record struct MarkInfo(decimal MarksAwarded, decimal MaxMark);

        // Pure formula application — Score/Level/Intervention from an already-resolved academic
        // average + attendance. Shared by the one-learner and batched paths above so they can
        // never drift apart.
        private static RiskData BuildRiskData(int learnerId, int subjectId, List<string> attendanceStatuses, decimal academicAverage)
        {
            var riskData = new RiskData { LearnerId = learnerId, SubjectId = subjectId, AcademicAverage = academicAverage };

            int presentCount = attendanceStatuses.Count(s => s == "Present");
            riskData.AttendancePercentage = attendanceStatuses.Count > 0
                ? (presentCount * 100m) / attendanceStatuses.Count
                : 100m;

            // 60% academics + 30% attendance + 10% trend (simplified to 0.5 for now)
            decimal academicFactor = 60m * (riskData.AcademicAverage / 100m);
            decimal attendanceFactor = 30m * (riskData.AttendancePercentage / 100m);
            decimal trendFactor = 10m * 0.5m;

            riskData.Score = 100 - (academicFactor + attendanceFactor + trendFactor);

            if (riskData.Score >= 55)
            {
                riskData.Level = "Critical";
                riskData.Intervention = "Urgent: Parent meeting required, intensive intervention needed";
            }
            else if (riskData.Score >= AtRiskThreshold)
            {
                riskData.Level = "High";
                riskData.Intervention = "Schedule tutoring sessions, monitor progress closely";
            }
            else if (riskData.Score >= 30)
            {
                riskData.Level = "Moderate";
                riskData.Intervention = "Provide extra support, encourage participation";
            }
            else
            {
                riskData.Level = "Low";
                riskData.Intervention = "Continue regular monitoring";
            }

            return riskData;
        }
    }
}
