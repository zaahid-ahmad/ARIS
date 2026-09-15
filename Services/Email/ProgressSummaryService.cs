using ARIS1.Data;
using ARIS1.Models;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services.Email
{
    public record ProgressReportBuild(int LearnerCount, int ParentCount, int OptedOutCount, string? SampleHtml, List<EmailMessage> Messages);

    // Builds per-parent term progress report emails (one per linked parent per learner). Uses the same numbers as
    // the rest of the app: WeightCalculationService term/year marks, RiskAssessmentService risk levels and
    // subject-scoped attendance, batched per subject rather than per learner.
    public class ProgressSummaryService
    {
        private readonly AppDbContext _dbContext;
        private readonly WeightCalculationService _weightCalculationService;
        private readonly RiskAssessmentService _riskAssessmentService;
        private readonly ParentRecipientService _recipientService;

        public ProgressSummaryService(AppDbContext dbContext, WeightCalculationService weightCalculationService,
            RiskAssessmentService riskAssessmentService, ParentRecipientService recipientService)
        {
            _dbContext = dbContext;
            _weightCalculationService = weightCalculationService;
            _riskAssessmentService = riskAssessmentService;
            _recipientService = recipientService;
        }

        public async Task<ProgressReportBuild> BuildAsync(int schoolId, IReadOnlyCollection<int> learnerIds, int term,
            string baseUri, string? senderUserId)
        {
            var schoolName = await _dbContext.Schools.Where(s => s.SchoolId == schoolId).Select(s => s.Name).FirstOrDefaultAsync();
            var currentYear = await _dbContext.Subjects
                .Where(s => s.SchoolId == schoolId)
                .Select(s => (int?)s.AcademicYear)
                .MaxAsync() ?? DateTime.Now.Year;

            var learners = await _dbContext.Learners.AsNoTracking()
                .Where(l => learnerIds.Contains(l.LearnerId) && l.User.SchoolId == schoolId)
                .Select(l => new { l.LearnerId, l.Grade, Name = l.User.Fullname })
                .ToDictionaryAsync(l => l.LearnerId);
            var ids = learners.Keys.ToList();

            var recipients = await _recipientService.ResolveAsync(ids, schoolId);
            if (recipients.Count == 0) return new ProgressReportBuild(ids.Count, 0, 0, null, new List<EmailMessage>());

            // Only learners that actually have a parent to email need their numbers computed.
            var reportLearnerIds = recipients.SelectMany(r => r.Children.Select(c => c.LearnerId)).Distinct().ToList();

            var enrollments = await _dbContext.LearnerSubjects.AsNoTracking()
                .Where(ls => reportLearnerIds.Contains(ls.LearnerId) && ls.Subject.AcademicYear == currentYear && ls.Subject.SchoolId == schoolId)
                .Select(ls => new { ls.LearnerId, ls.SubjectId, SubjectName = ls.Subject.Name, SubjectGrade = ls.Subject.Grade })
                .ToListAsync();

            var subjectIds = enrollments.Select(e => e.SubjectId).Distinct().ToList();
            var subjectsWithYearStructure = (await _dbContext.WeightingStructures
                .Where(ws => subjectIds.Contains(ws.SubjectId) && ws.Term == 0)
                .Select(ws => ws.SubjectId)
                .ToListAsync()).ToHashSet();

            var termMarks = new Dictionary<(int LearnerId, int SubjectId), decimal>();
            var yearMarks = new Dictionary<(int LearnerId, int SubjectId), decimal>();
            var riskLevels = new Dictionary<(int LearnerId, int SubjectId), string>();

            foreach (var subjectGroup in enrollments.GroupBy(e => e.SubjectId))
            {
                var subjectLearnerIds = subjectGroup.Select(e => e.LearnerId).ToList();
                var subjectGrade = subjectGroup.First().SubjectGrade;

                if (!(subjectGrade == 12 && term == 4))
                {
                    var termResults = await _weightCalculationService.CalculateWeightedTermMarksForSubject(subjectGroup.Key, term, subjectLearnerIds);
                    foreach (var (learnerId, result) in termResults.Where(kv => kv.Value.IsSuccessful))
                        termMarks[(learnerId, subjectGroup.Key)] = result.WeightedPercentage;
                }

                if (subjectsWithYearStructure.Contains(subjectGroup.Key))
                {
                    foreach (var learnerId in subjectLearnerIds)
                    {
                        var year = await _weightCalculationService.CalculateYearMark(learnerId, subjectGroup.Key);
                        if (year.IsSuccessful) yearMarks[(learnerId, subjectGroup.Key)] = year.YearPercentage;
                    }
                }

                var risk = await _riskAssessmentService.CalculateRiskScoresForSubject(subjectGroup.Key, subjectLearnerIds);
                foreach (var (learnerId, data) in risk)
                    riskLevels[(learnerId, subjectGroup.Key)] = data.Level;
            }

            var attendance = (await _dbContext.AttendanceRecords.AsNoTracking()
                    .Where(a => reportLearnerIds.Contains(a.LearnerId) && subjectIds.Contains(a.Session.SubjectId))
                    .GroupBy(a => new { a.LearnerId, a.Session.SubjectId })
                    .Select(g => new { g.Key.LearnerId, g.Key.SubjectId, Total = g.Count(), Present = g.Count(a => a.Status == "Present") })
                    .ToListAsync())
                .ToDictionary(a => (a.LearnerId, a.SubjectId), a => a.Total == 0 ? (decimal?)null : a.Present * 100m / a.Total);

            var concernTopics = (await _dbContext.Interventions.AsNoTracking()
                    .Where(i => reportLearnerIds.Contains(i.LearnerId) && subjectIds.Contains(i.Question.Assessment.SubjectId)
                                && (i.Level == "Critical" || i.Level == "Attention"))
                    .Select(i => new { i.LearnerId, i.Level, i.Topic, SubjectName = i.Question.Assessment.Subject.Name })
                    .ToListAsync())
                .GroupBy(i => i.LearnerId)
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(i => i.Level == "Critical" ? 0 : 1)
                    .Select(i => $"{i.SubjectName}: {i.Topic}")
                    .Distinct()
                    .Take(6)
                    .ToList());

            var messages = new List<EmailMessage>();
            string? sampleHtml = null;
            foreach (var parent in recipients)
            {
                foreach (var child in parent.Children)
                {
                    if (!learners.TryGetValue(child.LearnerId, out var learner)) continue;

                    var rows = enrollments
                        .Where(e => e.LearnerId == child.LearnerId)
                        .OrderBy(e => e.SubjectName)
                        .Select(e => new EmailTemplates.SummaryRow(
                            e.SubjectName,
                            termMarks.TryGetValue((e.LearnerId, e.SubjectId), out var tm) ? tm : null,
                            yearMarks.TryGetValue((e.LearnerId, e.SubjectId), out var ym) ? ym : null,
                            attendance.TryGetValue((e.LearnerId, e.SubjectId), out var att) ? att : null,
                            riskLevels.TryGetValue((e.LearnerId, e.SubjectId), out var rl) ? rl : "–"))
                        .ToList();

                    var marksUrl = new Uri(new Uri(baseUri), $"parent/marks/{child.LearnerId}").AbsoluteUri;
                    var rendered = EmailTemplates.ProgressSummary(parent.Fullname, learner.Name, learner.Grade, term, rows,
                        concernTopics.GetValueOrDefault(child.LearnerId) ?? new List<string>(), marksUrl, schoolName);

                    if (sampleHtml == null && !parent.OptedOut) sampleHtml = rendered.Html;

                    messages.Add(new EmailMessage(parent.Email, rendered.Subject, rendered.Html, rendered.Text,
                        EmailCategories.ProgressSummary, schoolId, parent.UserId, child.LearnerId, senderUserId));
                }
            }

            return new ProgressReportBuild(ids.Count, recipients.Count, recipients.Count(r => r.OptedOut), sampleHtml, messages);
        }
    }
}
