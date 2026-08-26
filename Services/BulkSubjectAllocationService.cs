using ARIS1.Data;
using ARIS1.Models;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public record ClassAllocationPreview(
        int Grade,
        string ClassName,
        int AcademicYear,
        List<string> SubjectNames,
        List<string> LearnerNames,
        int AlreadyEnrolledPairCount,
        int NewEnrollmentCount);

    public record ClassAllocationResult(int EnrollmentsCreated, int PairsSkipped);

    public class BulkSubjectAllocationService
    {
        private readonly AppDbContext _dbContext;

        public BulkSubjectAllocationService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<string>> GetClassNamesAsync(int schoolId, int grade)
        {
            return await _dbContext.Learners
                .AsNoTracking()
                .Where(l => l.Grade == grade && l.User.SchoolId == schoolId)
                .Select(l => l.ClassName)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
        }

        public async Task<List<Subject>> GetGradeSubjectsAsync(int schoolId, int grade)
        {
            return await _dbContext.Subjects
                .AsNoTracking()
                .Where(s => s.Grade == grade && s.SchoolId == schoolId)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<ClassAllocationPreview> PreviewAsync(int schoolId, int grade, string className, List<int> subjectIds, int academicYear)
        {
            var (learners, subjects) = await LoadAndValidateAsync(schoolId, grade, className, subjectIds);
            var existingPairCount = await CountExistingPairsAsync(learners, subjects);

            var totalPairs = learners.Count * subjects.Count;

            return new ClassAllocationPreview(
                grade,
                className,
                academicYear,
                subjects.Select(s => s.Name).ToList(),
                learners.Select(l => l.User!.Fullname).ToList(),
                existingPairCount,
                totalPairs - existingPairCount);
        }

        public async Task<ClassAllocationResult> AllocateAsync(int schoolId, int grade, string className, List<int> subjectIds, int academicYear)
        {
            var (learners, subjects) = await LoadAndValidateAsync(schoolId, grade, className, subjectIds);

            var existingPairs = await GetExistingPairsAsync(learners, subjects);

            var toInsert = new List<LearnerSubject>();
            foreach (var learner in learners)
            {
                foreach (var subject in subjects)
                {
                    if (!existingPairs.Contains((learner.LearnerId, subject.SubjectId)))
                    {
                        toInsert.Add(new LearnerSubject
                        {
                            LearnerId = learner.LearnerId,
                            SubjectId = subject.SubjectId,
                            AcademicYear = academicYear
                        });
                    }
                }
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            _dbContext.LearnerSubjects.AddRange(toInsert);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new ClassAllocationResult(toInsert.Count, existingPairs.Count);
        }

        // Grade integrity: learners are filtered to the requested grade, and any subject not
        // belonging to that same grade+school is rejected rather than silently dropped, so a
        // stale or tampered subject selection can never cross-enroll a learner into the wrong grade.
        private async Task<(List<Learner> learners, List<Subject> subjects)> LoadAndValidateAsync(
            int schoolId, int grade, string className, List<int> subjectIds)
        {
            var subjects = await _dbContext.Subjects
                .Where(s => subjectIds.Contains(s.SubjectId) && s.SchoolId == schoolId && s.Grade == grade)
                .ToListAsync();

            if (subjects.Count != subjectIds.Distinct().Count())
                throw new InvalidOperationException("One or more selected subjects do not belong to the selected grade.");

            var learners = await _dbContext.Learners
                .Include(l => l.User)
                .Where(l => l.Grade == grade && l.ClassName == className && l.User.SchoolId == schoolId)
                .OrderBy(l => l.User.Fullname)
                .ToListAsync();

            return (learners, subjects);
        }

        private async Task<HashSet<(int LearnerId, int SubjectId)>> GetExistingPairsAsync(List<Learner> learners, List<Subject> subjects)
        {
            var learnerIds = learners.Select(l => l.LearnerId).ToList();
            var subjectIds = subjects.Select(s => s.SubjectId).ToList();

            var pairs = await _dbContext.LearnerSubjects
                .Where(ls => learnerIds.Contains(ls.LearnerId) && subjectIds.Contains(ls.SubjectId))
                .Select(ls => new { ls.LearnerId, ls.SubjectId })
                .ToListAsync();

            return pairs.Select(p => (p.LearnerId, p.SubjectId)).ToHashSet();
        }

        private async Task<int> CountExistingPairsAsync(List<Learner> learners, List<Subject> subjects)
        {
            var pairs = await GetExistingPairsAsync(learners, subjects);
            return pairs.Count;
        }
    }
}
