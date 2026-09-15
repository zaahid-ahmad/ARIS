using ARIS1.Data;
using ARIS1.Models;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services.Email
{
    // Automatic parent risk alerts, evaluated when a teacher finishes capturing an assessment (Teacher/Marks.razor
    // "Done"), never on each auto-save. A learner's parents are emailed only when the learner's level for that
    // subject is worse than the level they were last alerted about (ParentAlertState), and only if the school
    // has ParentAlertsEnabled switched on.
    //
    // Effective level: Critical if the subject risk level is Critical or this assessment produced a Critical
    // intervention; High if the risk level is High; otherwise no alert (and any previous alert state is cleared,
    // so a later decline alerts again).
    public class ParentAlertService
    {
        private readonly AppDbContext _dbContext;
        private readonly RiskAssessmentService _riskAssessmentService;
        private readonly ParentRecipientService _recipientService;
        private readonly EmailService _emailService;

        public ParentAlertService(AppDbContext dbContext, RiskAssessmentService riskAssessmentService,
            ParentRecipientService recipientService, EmailService emailService)
        {
            _dbContext = dbContext;
            _riskAssessmentService = riskAssessmentService;
            _recipientService = recipientService;
            _emailService = emailService;
        }

        private static int Rank(string? level) => level switch { "Critical" => 2, "High" => 1, _ => 0 };

        // Returns the number of alert emails queued.
        public async Task<int> EvaluateAssessmentAsync(int assessmentId, IReadOnlyCollection<int> learnerIds, string baseUri)
        {
            if (learnerIds.Count == 0) return 0;

            var assessment = await _dbContext.Assessments.AsNoTracking()
                .Where(a => a.AssessmentId == assessmentId)
                .Select(a => new { a.SubjectId, SubjectName = a.Subject.Name, a.Subject.SchoolId })
                .FirstOrDefaultAsync();
            if (assessment == null) return 0;

            var school = await _dbContext.Schools.AsNoTracking()
                .Where(s => s.SchoolId == assessment.SchoolId)
                .Select(s => new { s.Name, s.ParentAlertsEnabled })
                .FirstOrDefaultAsync();
            if (school == null || !school.ParentAlertsEnabled) return 0;

            // Only learners actually enrolled in this subject.
            var ids = await _dbContext.LearnerSubjects
                .Where(ls => ls.SubjectId == assessment.SubjectId && learnerIds.Contains(ls.LearnerId))
                .Select(ls => ls.LearnerId)
                .ToListAsync();
            if (ids.Count == 0) return 0;

            var risk = await _riskAssessmentService.CalculateRiskScoresForSubject(assessment.SubjectId, ids);

            var interventions = await _dbContext.Interventions.AsNoTracking()
                .Where(i => ids.Contains(i.LearnerId) && i.Question.Assessment.SubjectId == assessment.SubjectId
                            && (i.Level == "Critical" || i.Level == "Attention"))
                .Select(i => new { i.LearnerId, i.Level, i.Topic, i.Question.AssessmentId })
                .ToListAsync();

            var criticalThisAssessment = interventions
                .Where(i => i.AssessmentId == assessmentId && i.Level == "Critical")
                .Select(i => i.LearnerId)
                .ToHashSet();

            var states = await _dbContext.ParentAlertStates
                .Where(s => s.SubjectId == assessment.SubjectId && ids.Contains(s.LearnerId))
                .ToDictionaryAsync(s => s.LearnerId);

            var toAlert = new Dictionary<int, string>();
            foreach (var learnerId in ids)
            {
                var riskLevel = risk.TryGetValue(learnerId, out var r) ? r.Level : null;
                string? effective = riskLevel == "Critical" || criticalThisAssessment.Contains(learnerId) ? "Critical"
                                  : riskLevel == "High" ? "High"
                                  : null;

                states.TryGetValue(learnerId, out var state);

                if (effective == null)
                {
                    if (state != null) _dbContext.ParentAlertStates.Remove(state);
                    continue;
                }

                if (state != null && Rank(state.LastAlertLevel) >= Rank(effective)) continue;

                toAlert[learnerId] = effective;
                if (state == null)
                {
                    _dbContext.ParentAlertStates.Add(new ParentAlertState
                    {
                        LearnerId = learnerId, SubjectId = assessment.SubjectId, LastAlertLevel = effective, LastAlertedUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    state.LastAlertLevel = effective;
                    state.LastAlertedUtc = DateTime.UtcNow;
                }
            }

            if (toAlert.Count == 0)
            {
                await _dbContext.SaveChangesAsync();
                return 0;
            }

            var recipients = await _recipientService.ResolveAsync(toAlert.Keys.ToList(), assessment.SchoolId);
            var messages = new List<EmailMessage>();
            foreach (var parent in recipients)
            {
                foreach (var child in parent.Children.Where(c => toAlert.ContainsKey(c.LearnerId)))
                {
                    var data = risk[child.LearnerId];
                    var topics = interventions
                        .Where(i => i.LearnerId == child.LearnerId)
                        .OrderBy(i => i.Level == "Critical" ? 0 : 1)
                        .Select(i => i.Topic)
                        .Distinct()
                        .Take(5)
                        .ToList();
                    var overviewUrl = new Uri(new Uri(baseUri), $"parent/overview/{child.LearnerId}").AbsoluteUri;

                    var rendered = EmailTemplates.RiskAlert(parent.Fullname, child.Name, assessment.SubjectName, toAlert[child.LearnerId],
                        data.Score, data.AcademicAverage, data.AttendancePercentage, topics, overviewUrl, school.Name);

                    messages.Add(new EmailMessage(parent.Email, rendered.Subject, rendered.Html, rendered.Text,
                        EmailCategories.RiskAlert, assessment.SchoolId, parent.UserId, child.LearnerId));
                }
            }

            // QueueManyAsync saves the pending ParentAlertState changes in the same SaveChanges.
            var logs = await _emailService.QueueManyAsync(messages);
            if (messages.Count == 0) await _dbContext.SaveChangesAsync();
            return logs.Count(l => l.Status == EmailStatuses.Queued);
        }
    }
}
