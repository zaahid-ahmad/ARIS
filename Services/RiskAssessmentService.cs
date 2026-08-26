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
            var riskData = new RiskData { LearnerId = learnerId, SubjectId = subjectId };

            var attendanceRecords = await _dbContext.AttendanceRecords
                .AsNoTracking()
                .Where(ar => ar.LearnerId == learnerId)
                .ToListAsync();

            int presentCount = attendanceRecords.Count(ar => ar.Status == "Present");
            riskData.AttendancePercentage = attendanceRecords.Count > 0
                ? (presentCount * 100m) / attendanceRecords.Count
                : 100m;

            var marks = await _dbContext.LearnerMarks
                .AsNoTracking()
                .Where(m => m.Assessment.SubjectId == subjectId && m.LearnerId == learnerId && !m.IsAbsent)
                .Include(m => m.Assessment)
                .ToListAsync();

            if (marks.Count == 0)
            {
                riskData.AcademicAverage = 0;
            }
            else
            {
                decimal totalPercentage = 0m;
                foreach (var mark in marks)
                {
                    decimal percentage = (mark.MarksAwarded / mark.Assessment.MaxMark) * 100m;
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
