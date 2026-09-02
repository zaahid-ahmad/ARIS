using ARIS1.Data;
using ARIS1.Models;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public record LearnerRolloverRow(
        int LearnerId,
        string LearnerName,
        int CurrentGrade,
        string CurrentClassName,
        string DefaultAction, // "Promote" or "Graduate"
        int? TargetClassId,
        string? TargetClassName,
        bool RequiresClassSelection);

    public record RolloverPreview(
        bool IsBlocked,
        string? BlockedReason,
        int EndingYear,
        int NewYear,
        int SubjectsToClone,
        List<LearnerRolloverRow> Learners);

    public record LearnerRolloverChoice(int LearnerId, string Action, int? TargetClassId);
    // Action: "Promote" | "Repeat" | "Graduate"

    public record RolloverResult(int Promoted, int Repeated, int Graduated, int SubjectsCloned, List<string> Warnings);

    public class YearRolloverService
    {
        private readonly AppDbContext _dbContext;
        private readonly RiskAssessmentService _riskAssessmentService;

        public YearRolloverService(AppDbContext dbContext, RiskAssessmentService riskAssessmentService)
        {
            _dbContext = dbContext;
            _riskAssessmentService = riskAssessmentService;
        }

        public async Task<RolloverPreview> PreviewAsync(int schoolId)
        {
            var (endingYear, newYear, blockedReason) = await ResolveYearsAsync(schoolId);
            if (blockedReason != null)
                return new RolloverPreview(true, blockedReason, endingYear, newYear, 0, new List<LearnerRolloverRow>());

            var subjectsToClone = await _dbContext.Subjects
                .CountAsync(s => s.SchoolId == schoolId && s.AcademicYear == endingYear);

            var learners = await _dbContext.Learners
                .Include(l => l.User)
                .Include(l => l.Class)
                .Where(l => l.User.SchoolId == schoolId && l.Status == "Active")
                .OrderBy(l => l.Grade).ThenBy(l => l.Class.Name).ThenBy(l => l.User.Fullname)
                .ToListAsync();

            var allClasses = await _dbContext.SchoolClasses
                .Where(c => c.SchoolId == schoolId)
                .ToListAsync();

            var rows = new List<LearnerRolloverRow>();
            foreach (var learner in learners)
            {
                if (learner.Grade == 12)
                {
                    rows.Add(new LearnerRolloverRow(
                        learner.LearnerId, learner.User.Fullname, learner.Grade, learner.Class.Name,
                        "Graduate", null, null, false));
                }
                else
                {
                    // Default target class: same letter name one grade up, if it exists.
                    var targetClass = allClasses.FirstOrDefault(c => c.Grade == learner.Grade + 1 && c.Name == learner.Class.Name);
                    rows.Add(new LearnerRolloverRow(
                        learner.LearnerId, learner.User.Fullname, learner.Grade, learner.Class.Name,
                        "Promote", targetClass?.ClassId, targetClass?.Name, targetClass == null));
                }
            }

            return new RolloverPreview(false, null, endingYear, newYear, subjectsToClone, rows);
        }

        public async Task<RolloverResult> ExecuteAsync(int schoolId, List<LearnerRolloverChoice> choices)
        {
            var (endingYear, newYear, blockedReason) = await ResolveYearsAsync(schoolId);
            if (blockedReason != null)
                throw new InvalidOperationException(blockedReason);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var subjectMap = await CloneSubjectCatalogAsync(schoolId, endingYear, newYear);

            var warnings = new List<string>();
            int promoted = 0, repeated = 0, graduated = 0;

            var learnerIds = choices.Select(c => c.LearnerId).ToList();
            var learners = await _dbContext.Learners
                .Include(l => l.User)
                .Where(l => learnerIds.Contains(l.LearnerId))
                .ToDictionaryAsync(l => l.LearnerId);

            var oldEnrollments = await _dbContext.LearnerSubjects
                .Where(ls => learnerIds.Contains(ls.LearnerId) && ls.Subject.AcademicYear == endingYear)
                .Select(ls => new { ls.LearnerId, ls.SubjectId, SubjectName = ls.Subject.Name })
                .ToListAsync();

            var enrollmentsByLearner = oldEnrollments
                .GroupBy(e => e.LearnerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Freeze a risk-score snapshot per (learner, subject) as this year closes out — one
            // batched CalculateRiskScoresForSubject call per distinct subject among the group,
            // same batching discipline as everywhere else risk scores are computed. This is what
            // Admin/PreviousYears.razor reads back later; it's independent of Promote/Repeat/
            // Graduate — every learner's ending-year subjects get a frozen snapshot regardless of
            // what happens to them next.
            foreach (var subjectGroup in oldEnrollments.GroupBy(e => e.SubjectId))
            {
                var subjectLearnerIds = subjectGroup.Select(e => e.LearnerId).ToList();
                var riskScores = await _riskAssessmentService.CalculateRiskScoresForSubject(subjectGroup.Key, subjectLearnerIds);

                foreach (var learnerId in subjectLearnerIds)
                {
                    var risk = riskScores[learnerId];
                    _dbContext.LearnerYearSubjectRisks.Add(new LearnerYearSubjectRisk
                    {
                        LearnerId = learnerId,
                        SubjectId = subjectGroup.Key,
                        AcademicYear = endingYear,
                        Score = risk.Score,
                        Level = risk.Level,
                        AcademicAverage = risk.AcademicAverage,
                        AttendancePercentage = risk.AttendancePercentage
                    });
                }
            }

            // (Grade, Name) -> cloned new-year Subject, for matching a learner's old enrollments forward.
            var newSubjectsByGradeName = subjectMap.Values
                .GroupBy(s => (s.Grade, s.Name))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var choice in choices)
            {
                if (!learners.TryGetValue(choice.LearnerId, out var learner))
                    continue;

                var endingGrade = learner.Grade;
                var endingClassId = learner.ClassId;

                string outcome = choice.Action switch
                {
                    "Promote" => "Promoted",
                    "Repeat" => "Repeated",
                    "Graduate" => "Graduated",
                    _ => throw new InvalidOperationException($"Unknown rollover action '{choice.Action}'.")
                };

                _dbContext.LearnerYearRecords.Add(new LearnerYearRecord
                {
                    LearnerId = learner.LearnerId,
                    AcademicYear = endingYear,
                    Grade = endingGrade,
                    ClassId = endingClassId,
                    Outcome = outcome
                });

                if (choice.Action == "Graduate")
                {
                    learner.Status = "Graduated";
                    learner.GraduatedAcademicYear = endingYear;
                    graduated++;
                    continue; // no new-year record, no re-enrollment
                }

                int newGrade = choice.Action == "Promote" ? endingGrade + 1 : endingGrade;
                int newClassId;
                if (choice.Action == "Promote")
                {
                    if (choice.TargetClassId == null)
                        throw new InvalidOperationException($"Learner {learner.LearnerId} has no target class resolved for promotion.");
                    newClassId = choice.TargetClassId.Value;
                    promoted++;
                }
                else
                {
                    newClassId = endingClassId; // repeat: stays in the same class bucket
                    repeated++;
                }

                learner.Grade = newGrade;
                learner.ClassId = newClassId;

                _dbContext.LearnerYearRecords.Add(new LearnerYearRecord
                {
                    LearnerId = learner.LearnerId,
                    AcademicYear = newYear,
                    Grade = newGrade,
                    ClassId = newClassId,
                    Outcome = "Active"
                });

                if (enrollmentsByLearner.TryGetValue(learner.LearnerId, out var oldEnrollmentsForLearner))
                {
                    foreach (var enrollment in oldEnrollmentsForLearner)
                    {
                        if (newSubjectsByGradeName.TryGetValue((newGrade, enrollment.SubjectName), out var newSubject))
                        {
                            _dbContext.LearnerSubjects.Add(new LearnerSubject
                            {
                                LearnerId = learner.LearnerId,
                                SubjectId = newSubject.SubjectId,
                                AcademicYear = newYear
                            });
                        }
                        else
                        {
                            warnings.Add($"{learner.User.Fullname} was enrolled in {enrollment.SubjectName} — no Grade {newGrade} equivalent found; enroll manually via Learner Enrollment.");
                        }
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new RolloverResult(promoted, repeated, graduated, subjectMap.Count, warnings);
        }

        private async Task<(int endingYear, int newYear, string? blockedReason)> ResolveYearsAsync(int schoolId)
        {
            var endingYear = await _dbContext.Subjects
                .Where(s => s.SchoolId == schoolId)
                .Select(s => (int?)s.AcademicYear)
                .MaxAsync() ?? DateTime.Now.Year;

            var newYear = DateTime.Now.Year;

            var alreadyRolled = await _dbContext.Subjects
                .AnyAsync(s => s.SchoolId == schoolId && s.AcademicYear == newYear);

            string? blockedReason = alreadyRolled
                ? $"This school has already been rolled over to {newYear}."
                : null;

            return (endingYear, newYear, blockedReason);
        }

        // Clones every Subject the school has for endingYear into newYear (same Name/Grade/Teacher —
        // Grade is copied unchanged, since every year still needs its own Grade 10/11/12 subjects; it's
        // the learners who move up a grade, not the subject-grade labels), along with each subject's
        // AssessmentType/WeightingStructure+WeightingNode tree/GradeBand catalog. Deliberately does NOT
        // clone Assessment/LearnerMark/AttendanceSession/AttendanceRecord/Intervention/LearnerSubject —
        // those are per-instance data for the year they were created in; the new year starts with none,
        // exactly like a subject does when first created today.
        private async Task<Dictionary<int, Subject>> CloneSubjectCatalogAsync(int schoolId, int endingYear, int newYear)
        {
            var oldSubjects = await _dbContext.Subjects
                .Where(s => s.SchoolId == schoolId && s.AcademicYear == endingYear)
                .ToListAsync();

            var subjectMap = new Dictionary<int, Subject>();
            foreach (var old in oldSubjects)
            {
                var clone = new Subject
                {
                    Name = old.Name,
                    Grade = old.Grade,
                    TeacherId = old.TeacherId,
                    AcademicYear = newYear,
                    SchoolId = schoolId
                };
                _dbContext.Subjects.Add(clone);
                subjectMap[old.SubjectId] = clone;
            }
            await _dbContext.SaveChangesAsync(); // need generated new SubjectIds before cloning children

            var oldSubjectIds = oldSubjects.Select(s => s.SubjectId).ToList();

            var oldAssessmentTypes = await _dbContext.AssessmentTypes
                .Where(at => oldSubjectIds.Contains(at.SubjectId))
                .ToListAsync();

            var assessmentTypeMap = new Dictionary<int, AssessmentType>();
            foreach (var oldAt in oldAssessmentTypes)
            {
                var clone = new AssessmentType
                {
                    SubjectId = subjectMap[oldAt.SubjectId].SubjectId,
                    Name = oldAt.Name,
                    WeightPercentage = oldAt.WeightPercentage,
                    Term = oldAt.Term
                };
                _dbContext.AssessmentTypes.Add(clone);
                assessmentTypeMap[oldAt.AssessmentTypeId] = clone;
            }
            await _dbContext.SaveChangesAsync(); // need generated new AssessmentTypeIds for WeightingNode remap

            var oldGradeBands = await _dbContext.GradeBands
                .Where(gb => oldSubjectIds.Contains(gb.SubjectId))
                .ToListAsync();

            foreach (var oldGb in oldGradeBands)
            {
                _dbContext.GradeBands.Add(new GradeBand
                {
                    SubjectId = subjectMap[oldGb.SubjectId].SubjectId,
                    MinPercentage = oldGb.MinPercentage,
                    MaxPercentage = oldGb.MaxPercentage,
                    APSLevel = oldGb.APSLevel,
                    Grade = oldGb.Grade
                });
            }

            var oldStructures = await _dbContext.WeightingStructures
                .Where(ws => oldSubjectIds.Contains(ws.SubjectId))
                .ToListAsync();

            foreach (var oldStructure in oldStructures)
            {
                var newStructure = new WeightingStructure
                {
                    SubjectId = subjectMap[oldStructure.SubjectId].SubjectId,
                    Term = oldStructure.Term,
                    Name = oldStructure.Name,
                    Description = oldStructure.Description,
                    IsActive = oldStructure.IsActive
                };
                _dbContext.WeightingStructures.Add(newStructure);
                await _dbContext.SaveChangesAsync(); // need generated new WeightingStructureId

                await CloneWeightingNodeTreeAsync(oldStructure.WeightingStructureId, newStructure.WeightingStructureId, assessmentTypeMap);
            }

            await _dbContext.SaveChangesAsync();
            return subjectMap;
        }

        // Clones a WeightingNode tree level by level (parents saved — and their generated Ids known —
        // before their children are created), since ParentNodeId must point at an already-persisted row.
        private async Task CloneWeightingNodeTreeAsync(int oldStructureId, int newStructureId, Dictionary<int, AssessmentType> assessmentTypeMap)
        {
            var oldNodes = await _dbContext.WeightingNodes
                .Where(n => n.WeightingStructureId == oldStructureId)
                .ToListAsync();

            var nodeIdMap = new Dictionary<int, int>(); // old WeightingNodeId -> new WeightingNodeId
            var remaining = new List<WeightingNode>(oldNodes);

            while (remaining.Count > 0)
            {
                var batch = remaining
                    .Where(n => n.ParentNodeId == null || nodeIdMap.ContainsKey(n.ParentNodeId.Value))
                    .ToList();

                if (batch.Count == 0)
                    break; // defensive: a well-formed tree never hits this

                var clones = new List<(WeightingNode Old, WeightingNode Clone)>();
                foreach (var oldNode in batch)
                {
                    var clone = new WeightingNode
                    {
                        WeightingStructureId = newStructureId,
                        ParentNodeId = oldNode.ParentNodeId.HasValue ? nodeIdMap[oldNode.ParentNodeId.Value] : null,
                        NodeType = oldNode.NodeType,
                        Name = oldNode.Name,
                        Weighting = oldNode.Weighting,
                        DisplayOrder = oldNode.DisplayOrder,
                        AssessmentTypeId = oldNode.AssessmentTypeId.HasValue
                            ? assessmentTypeMap[oldNode.AssessmentTypeId.Value].AssessmentTypeId
                            : null,
                        ReferencedTerm = oldNode.ReferencedTerm // term numbers don't change across years
                    };
                    _dbContext.WeightingNodes.Add(clone);
                    clones.Add((oldNode, clone));
                }

                await _dbContext.SaveChangesAsync();

                foreach (var (oldNode, clone) in clones)
                {
                    nodeIdMap[oldNode.WeightingNodeId] = clone.WeightingNodeId;
                    remaining.Remove(oldNode);
                }
            }
        }
    }
}
