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

        public RiskAssessmentService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<RiskData> CalculateRiskScore(int learnerId, int subjectId)
        {
            var attendanceStatuses = await _dbContext.AttendanceRecords
                .AsNoTracking()
                .Where(ar => ar.LearnerId == learnerId)
                .Select(ar => ar.Status)
                .ToListAsync();

            var marks = await _dbContext.LearnerMarks
                .AsNoTracking()
                .Where(m => m.Assessment.SubjectId == subjectId && m.LearnerId == learnerId && !m.IsAbsent)
                .Select(m => new MarkInfo(m.MarksAwarded, m.Assessment.MaxMark))
                .ToListAsync();

            return BuildRiskData(learnerId, subjectId, attendanceStatuses, marks);
        }

        /// <summary>
        /// Batched version of CalculateRiskScore for every learner in one subject — two DB
        /// round trips total instead of two per learner. Produces identical results to calling
        /// CalculateRiskScore once per learner; introduced because callers that walk every
        /// learner in a subject (Teacher/Dashboard.razor, Teacher/AtRisk.razor) were making
        /// hundreds of sequential round trips for subjects with many enrolled learners.
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

            var marksLookup = (await _dbContext.LearnerMarks
                    .AsNoTracking()
                    .Where(m => m.Assessment.SubjectId == subjectId && learnerIds.Contains(m.LearnerId) && !m.IsAbsent)
                    .Select(m => new { m.LearnerId, m.MarksAwarded, MaxMark = m.Assessment.MaxMark })
                    .ToListAsync())
                .GroupBy(m => m.LearnerId)
                .ToDictionary(g => g.Key, g => g.Select(x => new MarkInfo(x.MarksAwarded, x.MaxMark)).ToList());

            foreach (var learnerId in learnerIds)
            {
                results[learnerId] = BuildRiskData(
                    learnerId, subjectId,
                    attendanceLookup.GetValueOrDefault(learnerId, new List<string>()),
                    marksLookup.GetValueOrDefault(learnerId, new List<MarkInfo>()));
            }

            return results;
        }

        private readonly record struct MarkInfo(decimal MarksAwarded, decimal MaxMark);

        // Single source of truth for the scoring formula — shared by the one-learner and
        // batched paths above so they can never drift apart.
        private static RiskData BuildRiskData(int learnerId, int subjectId, List<string> attendanceStatuses, List<MarkInfo> marks)
        {
            var riskData = new RiskData { LearnerId = learnerId, SubjectId = subjectId };

            int presentCount = attendanceStatuses.Count(s => s == "Present");
            riskData.AttendancePercentage = attendanceStatuses.Count > 0
                ? (presentCount * 100m) / attendanceStatuses.Count
                : 100m;

            if (marks.Count == 0)
            {
                riskData.AcademicAverage = 0;
            }
            else
            {
                decimal totalPercentage = 0m;
                foreach (var mark in marks)
                {
                    decimal percentage = (mark.MarksAwarded / mark.MaxMark) * 100m;
                    totalPercentage += percentage;
                }
                riskData.AcademicAverage = totalPercentage / marks.Count;
            }

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
