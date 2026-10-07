using System.Collections.Concurrent;
using System.Diagnostics;
using ARIS1.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

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
        // When this subject's scores were calculated. Cached summaries keep their original time, so pages can say "Data as of".
        public DateTime ComputedAtUtc { get; set; }

        // Cached summaries are shared between requests and must never be mutated, so names go on a copy.
        public SubjectRiskSummary WithLearners(Dictionary<int, LearnerLabel> labels)
        {
            var copy = (SubjectRiskSummary)MemberwiseClone();
            copy.Learners = labels;
            return copy;
        }

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
        // Oldest calculation time among the subjects: the honest "Data as of" for the whole view.
        public DateTime AsOfUtc { get; set; } = DateTime.UtcNow;
        public int SubjectsFromCache { get; set; }
        public int SubjectsComputed { get; set; }

        public RiskAggregate Total => RiskOverviewService.Aggregate(Subjects);
        public RiskAggregate ForGrade(int grade) => RiskOverviewService.Aggregate(Subjects.Where(s => s.Grade == grade));
        public List<int> Grades => Subjects.Select(s => s.Grade).Distinct().OrderBy(g => g).ToList();
    }

    // Progress of a BuildAsync call: how many subjects are finished, out of how many.
    public record RiskBuildProgress(int Done, int Total, string? Current);

    // The single risk walk behind the Teacher Dashboard, the Admin Dashboard banner, the Admin Risk Overview and the
    // PDF report. Always the batched RiskAssessmentService call, once per subject — never per learner.
    //
    // Speed: each subject's result is cached (default 10 min, Risk:CacheMinutes) so the dashboard, overview and
    // report share one calculation, and subjects that are not cached are calculated in parallel, each worker in its
    // own scope (its own AppDbContext — one context cannot run concurrent queries). Cached summaries are shared
    // between requests and are never mutated. The cache is cleared per subject/school when marks, attendance or
    // enrolments change (InvalidateSubject/InvalidateSchool); the time limit is only a safety net.
    public class RiskOverviewService
    {
        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<RiskOverviewService> _log;
        private readonly TimeSpan _cacheDuration;
        private readonly int _maxParallelism;

        // How long a page waits for BuildAsync before it stops waiting and says so (the calculation itself keeps running
        // and is reused). Shared by the Admin Dashboard banner, the Risk Overview and the report's data stage.
        public TimeSpan LoadTimeLimit { get; }

        // One token per school: cancelling it evicts every cached subject of that school at once.
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> SchoolTokens = new();
        private static readonly object CreateLock = new();
        private static long _subjectComputations;

        // Diagnostics (and tests): how many subject calculations have actually run in this process.
        public static long SubjectComputations => Interlocked.Read(ref _subjectComputations);

        public RiskOverviewService(AppDbContext db, IMemoryCache cache, IServiceScopeFactory scopes,
            IConfiguration config, ILogger<RiskOverviewService> log)
        {
            _db = db;
            _cache = cache;
            _scopes = scopes;
            _log = log;
            _cacheDuration = TimeSpan.FromMinutes(Math.Max(0.01, config.GetValue("Risk:CacheMinutes", 10.0)));
            _maxParallelism = Math.Max(1, config.GetValue("Risk:MaxParallelism", Math.Min(4, Environment.ProcessorCount)));
            LoadTimeLimit = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Risk:LoadTimeoutSeconds", 120)));
        }

        private sealed record SubjectInfo(int SubjectId, string Name, int Grade, string TeacherName, int SchoolId);

        // Current academic year for a school: same resolution as YearRolloverService.ResolveYearsAsync and the Teacher pages.
        public async Task<int> ResolveCurrentYearAsync(int schoolId, CancellationToken ct = default) =>
            await _db.Subjects.Where(s => s.SchoolId == schoolId)
                .Select(s => (int?)s.AcademicYear).MaxAsync(ct) ?? DateTime.Now.Year;

        // Cache control -------------------------------------------------------------------------------------------

        public void InvalidateSubject(int subjectId) => _cache.Remove(Key(subjectId));

        public void InvalidateSchool(int schoolId)
        {
            if (SchoolTokens.TryRemove(schoolId, out var cts)) cts.Cancel();
        }

        private static string Key(int subjectId) => $"risk:subject:{subjectId}";

        // Every filter is applied on top of the mandatory school + current-year scope, so a subjectId/teacherId/grade
        // from another school or year simply matches nothing.
        public async Task<SchoolRiskOverview> BuildAsync(int schoolId, int? teacherId = null, int? grade = null,
            int? subjectId = null, bool includeLearnerNames = false, bool useCache = true,
            IProgress<RiskBuildProgress>? progress = null, CancellationToken ct = default)
        {
            // One log line per call, whatever the outcome, so "why is the overview slow / stuck?" can be answered from the
            // log. A call that never completes (the case that matters) is logged when the caller stops waiting.
            var sw = Stopwatch.StartNew();
            try
            {
                var overview = await BuildCoreAsync(schoolId, teacherId, grade, subjectId, includeLearnerNames, useCache, progress, ct);
                _log.Log(SlowLevel(sw),
                    "Risk overview for school {SchoolId}: {Subjects} subjects, {Computed} calculated, {Cached} from cache, in {Elapsed} ms",
                    schoolId, overview.Subjects.Count, overview.SubjectsComputed, overview.SubjectsFromCache, sw.ElapsedMilliseconds);
                return overview;
            }
            catch (OperationCanceledException)
            {
                // The caller left the page or hit its time limit; the shared calculation itself carries on.
                _log.Log(SlowLevel(sw), "Risk overview for school {SchoolId}: caller stopped waiting after {Elapsed} ms",
                    schoolId, sw.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Risk overview for school {SchoolId} failed after {Elapsed} ms", schoolId, sw.ElapsedMilliseconds);
                throw;
            }
        }

        private static LogLevel SlowLevel(Stopwatch sw) =>
            sw.Elapsed > TimeSpan.FromSeconds(10) ? LogLevel.Warning : LogLevel.Information;

        private async Task<SchoolRiskOverview> BuildCoreAsync(int schoolId, int? teacherId, int? grade,
            int? subjectId, bool includeLearnerNames, bool useCache,
            IProgress<RiskBuildProgress>? progress, CancellationToken ct)
        {
            var school = await _db.Schools.AsNoTracking()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => new { s.Name, s.Code })
                .FirstOrDefaultAsync(ct);

            var year = await ResolveCurrentYearAsync(schoolId, ct);
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
                .Select(s => new SubjectInfo(s.SubjectId, s.Name, s.Grade, s.Teacher.User.Fullname, schoolId))
                .ToListAsync(ct);
            if (subjects.Count == 0) return overview;

            var summaries = new SubjectRiskSummary[subjects.Count];
            var done = 0;
            var fromCache = 0;
            progress?.Report(new RiskBuildProgress(0, subjects.Count, null));

            await Parallel.ForEachAsync(Enumerable.Range(0, subjects.Count),
                new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
                async (i, token) =>
                {
                    var (task, cached) = GetSubject(subjects[i], useCache);
                    if (cached) Interlocked.Increment(ref fromCache);
                    // Waiting can be cancelled by this caller; the shared calculation itself is never cancelled.
                    summaries[i] = await task.WaitAsync(token);
                    progress?.Report(new RiskBuildProgress(Interlocked.Increment(ref done), subjects.Count, subjects[i].Name));
                });

            overview.Subjects = summaries.ToList();
            overview.SubjectsFromCache = fromCache;
            overview.SubjectsComputed = subjects.Count - fromCache;
            overview.AsOfUtc = summaries.Min(s => s.ComputedAtUtc);

            if (includeLearnerNames)
            {
                var allLearnerIds = overview.Subjects.SelectMany(s => s.Risks.Keys).Distinct().ToList();
                var labels = await _db.Learners.AsNoTracking()
                    .Where(l => allLearnerIds.Contains(l.LearnerId))
                    .Select(l => new { l.LearnerId, l.User.Fullname, ClassName = l.Class.Name, l.Grade })
                    .ToListAsync(ct);
                var byId = labels.ToDictionary(l => l.LearnerId, l => new LearnerLabel(l.Fullname, $"{l.Grade}{l.ClassName}"));
                // Names go on copies: the cached summaries are shared and stay name-free.
                overview.Subjects = overview.Subjects
                    .Select(s => s.WithLearners(s.Risks.Keys.Where(byId.ContainsKey).ToDictionary(id => id, id => byId[id])))
                    .ToList();
            }

            return overview;
        }

        // Returns the (possibly shared) calculation for one subject and whether it came from the cache.
        private (Task<SubjectRiskSummary> Task, bool Cached) GetSubject(SubjectInfo subject, bool useCache)
        {
            if (!useCache) return (ComputeSubjectAsync(subject), false);

            var key = Key(subject.SubjectId);
            Lazy<Task<SubjectRiskSummary>> lazy;
            bool cached;
            // Atomic get-or-create so two simultaneous requests share one calculation instead of both starting one.
            lock (CreateLock)
            {
                cached = _cache.TryGetValue(key, out Lazy<Task<SubjectRiskSummary>>? existing) && existing != null;
                if (cached)
                {
                    lazy = existing!;
                }
                else
                {
                    lazy = new Lazy<Task<SubjectRiskSummary>>(() => ComputeSubjectAsync(subject), LazyThreadSafetyMode.ExecutionAndPublication);
                    var token = SchoolTokens.GetOrAdd(subject.SchoolId, _ => new CancellationTokenSource()).Token;
                    _cache.Set(key, lazy, new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(_cacheDuration)
                        .AddExpirationToken(new CancellationChangeToken(token)));
                }
            }

            var task = lazy.Value;
            // A failed calculation must not be remembered.
            task.ContinueWith(t => { if (t.IsFaulted || t.IsCanceled) _cache.Remove(key); },
                CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion, TaskScheduler.Default);
            return (task, cached);
        }

        // Own scope per calculation: a scoped AppDbContext cannot be used by two operations at once.
        private async Task<SubjectRiskSummary> ComputeSubjectAsync(SubjectInfo subject)
        {
            Interlocked.Increment(ref _subjectComputations);
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var risk = scope.ServiceProvider.GetRequiredService<RiskAssessmentService>();

            var learnerIds = await db.LearnerSubjects.AsNoTracking()
                .Where(ls => ls.SubjectId == subject.SubjectId)
                .Select(ls => ls.LearnerId)
                .ToListAsync();

            var summary = new SubjectRiskSummary
            {
                SubjectId = subject.SubjectId,
                SubjectName = subject.Name,
                Grade = subject.Grade,
                TeacherName = subject.TeacherName,
                TotalLearners = learnerIds.Count
            };

            if (learnerIds.Count > 0)
            {
                summary.Risks = await risk.CalculateRiskScoresForSubject(subject.SubjectId, learnerIds);
                foreach (var learnerId in learnerIds)
                    summary.Counts[summary.Risks[learnerId].Level]++;
            }

            summary.ComputedAtUtc = DateTime.UtcNow;
            _log.LogDebug("Risk calculated for subject {SubjectId} ({Name}, grade {Grade}): {Learners} learners", subject.SubjectId, subject.Name, subject.Grade, learnerIds.Count);
            return summary;
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
