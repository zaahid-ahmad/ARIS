using ARIS1.Data;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services.Email
{
    public record ParentChild(int LearnerId, string Name);

    public record ParentRecipient(int ParentId, string UserId, string Fullname, string Email, bool OptedOut, List<ParentChild> Children);

    // Resolves learners → linked parents (ParentLearner), one recipient per parent even when several of their
    // children are in the selection. Only active parent accounts in the given school are returned.
    public class ParentRecipientService
    {
        private readonly AppDbContext _dbContext;

        public ParentRecipientService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<ParentRecipient>> ResolveAsync(IReadOnlyCollection<int> learnerIds, int schoolId)
        {
            if (learnerIds.Count == 0) return new List<ParentRecipient>();

            var links = await _dbContext.ParentLearners
                .AsNoTracking()
                .Where(pl => learnerIds.Contains(pl.LearnerId)
                             && pl.Parent.User.IsActive
                             && pl.Parent.User.SchoolId == schoolId
                             && pl.Learner.User.SchoolId == schoolId)
                .Select(pl => new
                {
                    pl.ParentId,
                    pl.Parent.UserId,
                    ParentName = pl.Parent.User.Fullname,
                    pl.Parent.User.Email,
                    pl.Parent.ReceiveNotificationEmails,
                    pl.LearnerId,
                    LearnerName = pl.Learner.User.Fullname
                })
                .ToListAsync();

            return links
                .GroupBy(l => l.ParentId)
                .Select(g =>
                {
                    var first = g.First();
                    return new ParentRecipient(first.ParentId, first.UserId, first.ParentName, first.Email ?? string.Empty,
                        !first.ReceiveNotificationEmails,
                        g.Select(l => new ParentChild(l.LearnerId, l.LearnerName)).DistinctBy(c => c.LearnerId).OrderBy(c => c.Name).ToList());
                })
                .OrderBy(r => r.Fullname)
                .ToList();
        }
    }
}
