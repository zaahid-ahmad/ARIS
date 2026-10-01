using ARIS1.Data;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    // One ordered risk palette for every chart/legend/PDF. Risk level is an ordered state (good -> critical),
    // not a categorical series, so these are the fixed status colours; the text label always accompanies them.
    public static class RiskPalette
    {
        public static readonly (string Level, string Color)[] Levels =
        {
            ("Critical", "#d03b3b"),
            ("High", "#ec835a"),
            ("Moderate", "#fab219"),
            ("Low", "#0ca30c")
        };

        public static string Color(string level) => level switch
        {
            "Critical" => "#d03b3b",
            "High" => "#ec835a",
            "Moderate" => "#fab219",
            _ => "#0ca30c" // Low
        };

        // 0 = worst. Used to pick a learner's highest-risk level across subjects.
        public static int Rank(string level) => level switch
        {
            "Critical" => 0,
            "High" => 1,
            "Moderate" => 2,
            _ => 3
        };
    }

    // Band counts for a set of subjects. Learners are counted ONCE each, by their highest-risk subject, so
    // Total is a distinct-learner count and AtRisk (score >= RiskAssessmentService.AtRiskThreshold in at least
    // one subject) is the same distinct count the Teacher Dashboard banner shows.
    public class RiskAggregate
    {
        public int Total { get; set; }
        public int AtRisk { get; set; }
        public Dictionary<string, int> Counts { get; set; } = RiskPalette.Levels.ToDictionary(l => l.Level, _ => 0);

        public int Count(string level) => Counts.TryGetValue(level, out var c) ? c : 0;
        public decimal AtRiskPercent => Total == 0 ? 0m : Math.Round(AtRisk * 100m / Total, 1);
        public List<(string Level, int Count, string Color)> Bands =>
            RiskPalette.Levels.Select(l => (l.Level, Count(l.Level), l.Color)).ToList();
    }

    public class SubjectRiskSummary
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int Grade { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public int TotalLearners { get; set; }
        public Dictionary<string, int> Counts { get; set; } = RiskPalette.Levels.ToDictionary(l => l.Level, _ => 0);
        public Dictionary<int, RiskData> Risks { get; set; } = new();
        // Only filled when BuildAsync is asked for names (the PDF's optional appendix).
        public Dictionary<int, LearnerLabel> Learners { get; set; } = new();

        public int Count(string level) => Counts.TryGetValue(level, out var c) ? c : 0;
        public int AtRisk => Risks.Values.Count(r => r.Score >= RiskAssessmentService.AtRiskThreshold);
        public decimal AtRiskPercent => TotalLearners == 0 ? 0m : Math.Round(AtRisk * 100m / TotalLearners, 1);
        public List<(string Level, int Count, string Color)> Bands =>
            RiskPalette.Levels.Select(l => (l.Level, Count(l.Level), l.Color)).ToList();
    }

    public record LearnerLabel(string Name, string ClassName);

    public class SchoolRiskOverview
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public string SchoolCode { get; set; } = string.Empty;
        public int AcademicYear { get; set; }
        public List<SubjectRiskSummary> Subjects { get; set; } = new();

        public RiskAggregate Total => RiskOverviewService.Aggregate(Subjects);
        public RiskAggregate ForGrade(int grade) => RiskOverviewService.Aggregate(Subjects.Where(s => s.Grade == grade));
        public List<int> Grades => Subjects.Select(s => s.Grade).Distinct().OrderBy(g => g).ToList();
    }

    // The single risk walk behind the Teacher Dashboard, the Admin Risk Overview and the PDF report. Always the
    // batched RiskAssessmentService call, once per subject — never per learner.
    public class RiskOverviewService
    {
        private readonly AppDbContext _db;
        private readonly RiskAssessmentService _risk;

        public RiskOverviewService(AppDbContext db, RiskAssessmentService risk)
        {
            _db = db;
            _risk = risk;
        }

        // Current academic year for a school: same resolution as YearRolloverService.ResolveYearsAsync and the Teacher pages.
        public async Task<int> ResolveCurrentYearAsync(int schoolId) =>
            await _db.Subjects.Where(s => s.SchoolId == schoolId)
                .Select(s => (int?)s.AcademicYear).MaxAsync() ?? DateTime.Now.Year;

        // Every filter is applied on top of the mandatory school + current-year scope, so a subjectId/teacherId/grade
        // from another school or year simply matches nothing.
        public async Task<SchoolRiskOverview> BuildAsync(int schoolId, int? teacherId = null, int? grade = null,
            int? subjectId = null, bool includeLearnerNames = false)
        {
            var school = await _db.Schools.AsNoTracking()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => new { s.Name, s.Code })
                .FirstOrDefaultAsync();

            var year = await ResolveCurrentYearAsync(schoolId);
            var overview = new SchoolRiskOverview
            {
                SchoolId = schoolId,
                SchoolName = school?.Name ?? string.Empty,
                SchoolCode = school?.Code ?? string.Empty,
                AcademicYear = year
            };

            var query = _db.Subjects.AsNoTracking()
                .Where(s => s.SchoolId == schoolId && s.AcademicYear == year);
            if (teacherId.HasValue) query = query.Where(s => s.TeacherId == teacherId.Value);
            if (grade.HasValue) query = query.Where(s => s.Grade == grade.Value);
            if (subjectId.HasValue) query = query.Where(s => s.SubjectId == subjectId.Value);

            var subjects = await query
                .OrderBy(s => s.Grade).ThenBy(s => s.Name)
                .Select(s => new { s.SubjectId, s.Name, s.Grade, TeacherName = s.Teacher.User.Fullname })
                .ToListAsync();
            if (subjects.Count == 0) return overview;

            // One query for every enrolment, grouped in memory.
            var subjectIds = subjects.Select(s => s.SubjectId).ToList();
            var enrolments = (await _db.LearnerSubjects.AsNoTracking()
                    .Where(ls => subjectIds.Contains(ls.SubjectId))
                    .Select(ls => new { ls.SubjectId, ls.LearnerId })
                    .ToListAsync())
                .GroupBy(e => e.SubjectId)
                .ToDictionary(g => g.Key, g => g.Select(e => e.LearnerId).ToList());

            foreach (var s in subjects)
            {
                var learnerIds = enrolments.TryGetValue(s.SubjectId, out var ids) ? ids : new List<int>();
                var summary = new SubjectRiskSummary
                {
                    SubjectId = s.SubjectId,
                    SubjectName = s.Name,
                    Grade = s.Grade,
                    TeacherName = s.TeacherName,
                    TotalLearners = learnerIds.Count
                };

                if (learnerIds.Count > 0)
                {
                    summary.Risks = await _risk.CalculateRiskScoresForSubject(s.SubjectId, learnerIds);
                    foreach (var learnerId in learnerIds)
                        summary.Counts[summary.Risks[learnerId].Level]++;
                }

                overview.Subjects.Add(summary);
            }

            if (includeLearnerNames)
            {
                var allLearnerIds = overview.Subjects.SelectMany(s => s.Risks.Keys).Distinct().ToList();
                var labels = await _db.Learners.AsNoTracking()
                    .Where(l => allLearnerIds.Contains(l.LearnerId))
                    .Select(l => new { l.LearnerId, l.User.Fullname, ClassName = l.Class.Name, l.Grade })
                    .ToListAsync();
                var byId = labels.ToDictionary(l => l.LearnerId, l => new LearnerLabel(l.Fullname, $"{l.Grade}{l.ClassName}"));
                foreach (var summary in overview.Subjects)
                    foreach (var id in summary.Risks.Keys)
                        if (byId.TryGetValue(id, out var label)) summary.Learners[id] = label;
            }

            return overview;
        }

        public static RiskAggregate Aggregate(IEnumerable<SubjectRiskSummary> subjects)
        {
            // Highest-risk (highest-score) result per distinct learner across the given subjects.
            var worst = new Dictionary<int, RiskData>();
            foreach (var risk in subjects.SelectMany(s => s.Risks.Values))
                if (!worst.TryGetValue(risk.LearnerId, out var current) || risk.Score > current.Score)
                    worst[risk.LearnerId] = risk;

            var aggregate = new RiskAggregate { Total = worst.Count };
            foreach (var risk in worst.Values)
            {
                aggregate.Counts[risk.Level]++;
                if (risk.Score >= RiskAssessmentService.AtRiskThreshold) aggregate.AtRisk++;
            }
            return aggregate;
        }
    }
}
