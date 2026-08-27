## Context
 
This is Phase 2 of the Parent-account feature — **authorization logic only.** No Razor pages, no NavMenu, no Home.razor, no new Program.cs registrations. Those come in Phase 3.
 
The problem this phase solves: every existing role in ARIS is scoped by *breadth* (`SchoolAuthorizationService.HasAccessToSchool`/`HasAccessToSubject` check school membership), but nothing today checks *individual ownership* — "does this specific parent have the right to see this specific learner's data." Without this check, any Parent account could view any learner's data just by knowing or guessing a `LearnerId`. This must be deny-by-default: every failure path (user not found, not a Parent, no linked children, learner not among their linked children) returns `false`, never throws, and never defaults to allowing access.
 
## Required change
 
**`Services/SchoolAuthorizationService.cs`** — add two new methods to the existing class. Do not create a new service class, do not modify `GetCurrentUserSchoolId`, `HasAccessToSchool`, or `HasAccessToSubject`.
 
Add `using Microsoft.EntityFrameworkCore;` to the file's usings if it isn't already there (needed for `AsNoTracking`/`FirstOrDefaultAsync`/`ToListAsync`).
 
Add these two methods, in this exact form:
 
```csharp
/// <summary>
/// Returns the LearnerIds this parent is linked to, via the ParentLearner table.
/// Empty list if the user isn't found, isn't in the Parent role, or has no linked
/// children — never throws.
/// </summary>
public async Task<List<int>> GetAccessibleLearnerIds(string? userName)
{
    if (string.IsNullOrEmpty(userName))
        return new List<int>();
 
    var user = await _userManager.FindByNameAsync(userName);
    if (user == null)
        return new List<int>();
 
    // Defense in depth: every Parent-facing page will also be gated by
    // [Authorize(Roles = "Parent")], but don't let a stale Parent/ParentLearner
    // row grant access if this user's role assignment has since changed.
    if (!await _userManager.IsInRoleAsync(user, "Parent"))
        return new List<int>();
 
    var parent = await _context.Parents
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.UserId == user.Id);
 
    if (parent == null)
        return new List<int>();
 
    return await _context.ParentLearners
        .AsNoTracking()
        .Where(pl => pl.ParentId == parent.ParentId)
        .Select(pl => pl.LearnerId)
        .ToListAsync();
}
 
/// <summary>
/// Validates that the given user (must be in the Parent role) is linked to the specific
/// learner via the ParentLearner table. Deny-by-default. This is a resource-level check
/// and does not replace [Authorize(Roles = "Parent")] on the page — both are required
/// together, the same way HasAccessToSubject is used alongside role attributes elsewhere
/// in this codebase.
/// </summary>
public async Task<bool> HasAccessToLearner(string? userName, int learnerId)
{
    var accessibleIds = await GetAccessibleLearnerIds(userName);
    return accessibleIds.Contains(learnerId);
}
```
 
Note `HasAccessToLearner` deliberately calls `GetAccessibleLearnerIds` rather than duplicating the lookup — one source of truth for "what can this parent see," so the two methods can never silently disagree with each other.
 
## What NOT to touch
 
- No changes to `Program.cs` (the service is already registered — you're only adding methods to an existing class, not a new one).
- No changes to any file under `Components/`.
- No changes to `Models/` or `Data/AppDbContext.cs` (that's Phase 1, already done).
- No new service classes, no interfaces.
## Steps to run, in order
 
1. Make the code change above.
2. `dotnet build` — confirm it compiles clean.
3. **Prove both the positive and negative case against real data in the dev database.** There's no Parent-facing UI yet (that's Phase 3), so you'll need a temporary way to call these two methods directly — e.g. a scratch block added to `Program.cs` guarded by `if (app.Environment.IsDevelopment())` that runs once at startup and writes results to the console, or an equivalent throwaway harness. Use whatever's fastest, but:
   - Create (or reuse, if similar dev/test accounts already exist in this database) two Parent users and at least two Learner records.
   - Link Parent A to Learner 1 only, and Parent B to Learner 2 only, via `ParentLearner` rows.
   - Assert and print all of the following:
     - `HasAccessToLearner("parentA-username", learner1Id)` → **true**
     - `HasAccessToLearner("parentA-username", learner2Id)` → **false**
     - `HasAccessToLearner("parentB-username", learner2Id)` → **true**
     - `HasAccessToLearner("parentB-username", learner1Id)` → **false**
     - `HasAccessToLearner("admin@aris.com", learner1Id)` → **false** (not a Parent at all)
     - `GetAccessibleLearnerIds("parentA-username")` → contains exactly `[learner1Id]`
     - `HasAccessToLearner("parentA-username", 999999)` → **false** (nonexistent learner id)
4. **Completely remove the temporary harness** once all six assertions pass — this phase must leave no scratch/debug code behind.
5. Run `git diff --stat` and confirm the only file listed is `Services/SchoolAuthorizationService.cs`. If anything else shows up (including a leftover scratch file), fix it before reporting done.
Report back: the exact diff on `SchoolAuthorizationService.cs`, the six assertion results from step 3, and the output of `git diff --stat` from step 5.