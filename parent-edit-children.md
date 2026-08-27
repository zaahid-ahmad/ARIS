# Prompt for Claude Code — ARIS Parent Account, Phase 4 (Edit Linked Children)

Copy everything below the line into Claude Code, run from the ARIS1 project root. This assumes Phases 1–3 are already applied — `Models/Parent.cs`, `Models/ParentLearner.cs`, `SchoolAuthorizationService.GetAccessibleLearnerIds`/`HasAccessToLearner`, and the `Parent/` pages all already exist in this codebase. If any of those are missing, stop and tell me instead of improvising them.

I have verified the current, real contents of `Components/Pages/Admin/UserManagement.razor` before writing this prompt — the code below is written against what's actually in that file today, not a guess. Read the file yourself first anyway before editing, to confirm nothing has changed since.

---

## Context

This is Phase 4: let an Admin add or remove children linked to an **existing** Parent account, the same way "Edit Class" already lets an Admin fix a Learner's class after the fact. This is purely additive to `Components/Pages/Admin/UserManagement.razor` — no other file changes.

**This file already has an established, working pattern for exactly this kind of feature** — study it before writing anything: the `learnerByUserId` batch-load in `LoadUsers()`, the `ShowEditClass`/`ConfirmEditClass`/`CancelEditClass` trio, the "EDIT CLASS MODAL" markup block, and — most importantly — the `isImporting` reentrancy guard on `HandleFileSelected`, which exists specifically because **an `async` event handler in Blazor Server can be invoked a second time before its first `await` finishes**, since a double-click dispatches two separate UI events and only a synchronous handler body is safe from overlap. That exact bug class was already found and fixed three times in this codebase (`AtRisk.razor`, `LearnerProfiles.razor`, and this file's own bulk import). Every new async handler you add in this phase — opening the modal, searching, adding, removing — needs the same style of guard. This is the main thing "iron clad" means for this phase: not just working once, but refusing to corrupt state when a button is double-clicked.

## Required changes — all inside `Components/Pages/Admin/UserManagement.razor`

### 1. Batch-load parents alongside learners

In `LoadUsers()`, right after the existing `learnerByUserId` line, add the equivalent for parents:

```csharp
learnerByUserId = await DbContext.Learners
    .Where(l => userIds.Contains(l.UserId))
    .ToDictionaryAsync(l => l.UserId);

parentByUserId = await DbContext.Parents
    .Where(p => userIds.Contains(p.UserId))
    .ToDictionaryAsync(p => p.UserId);
```

Add the matching field near the other dictionaries: `private Dictionary<string, Parent> parentByUserId = new();`

### 2. Table row — add an "Edit Children" button for Parent rows

In the `<tbody>` loop, alongside the existing `learner` lookup, add:

```csharp
var parent = parentByUserId.GetValueOrDefault(user.Id);
```

And in the Actions `<td>`, alongside the existing `@if (learner != null)` block for "Edit Class", add a sibling block:

```razor
@if (parent != null)
{
    <button class="btn btn-outline-info btn-sm ms-1"
            @onclick="() => ShowEditChildren(parent.ParentId, user.Fullname)">
        Edit Children
    </button>
}
```

### 3. New modal — place it after the existing "EDIT CLASS MODAL" block, same structural style

```razor
<!-- EDIT CHILDREN MODAL -->
@if (parentToEditId != null)
{
    <div class="modal d-block" style="background-color: rgba(0,0,0,0.5);">
        <div class="modal-dialog modal-lg">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Linked Children for @parentToEditName</h5>
                    <button type="button" class="btn-close" @onclick="CancelEditChildren"></button>
                </div>
                <div class="modal-body">
                    @if (!string.IsNullOrEmpty(editChildrenError))
                    {
                        <div class="alert alert-danger">@editChildrenError</div>
                    }

                    <h6>Currently Linked</h6>
                    @if (isLoadingChildren)
                    {
                        <p><em>Loading...</em></p>
                    }
                    else if (linkedChildren.Count == 0)
                    {
                        <p class="text-muted">No children linked yet.</p>
                    }
                    else
                    {
                        <ul class="list-group mb-3">
                            @foreach (var child in linkedChildren)
                            {
                                <li class="list-group-item d-flex justify-content-between align-items-center">
                                    @child.User.Fullname (Grade @child.Grade — @child.ClassName)
                                    <button class="btn btn-outline-danger btn-sm"
                                            @onclick="() => RemoveChild(child.LearnerId)"
                                            disabled="@isMutatingChildren">
                                        Remove
                                    </button>
                                </li>
                            }
                        </ul>
                    }

                    <hr />
                    <h6>Add a Child</h6>
                    <div class="d-flex gap-2 mb-2">
                        <input class="form-control" placeholder="Search by learner name..."
                               @bind="childSearchTerm" autocomplete="off" />
                        <button class="btn btn-primary" @onclick="SearchAvailableChildren" disabled="@isSearchingChildren">
                            Search
                        </button>
                    </div>
                    @if (isSearchingChildren)
                    {
                        <p><em>Searching...</em></p>
                    }
                    else if (childSearchResults != null)
                    {
                        @if (childSearchResults.Count == 0)
                        {
                            <p class="text-muted">No matching, unlinked learners found in your school.</p>
                        }
                        else
                        {
                            <ul class="list-group">
                                @foreach (var candidate in childSearchResults)
                                {
                                    <li class="list-group-item d-flex justify-content-between align-items-center">
                                        @candidate.User.Fullname (Grade @candidate.Grade — @candidate.ClassName)
                                        <button class="btn btn-outline-success btn-sm"
                                                @onclick="() => AddChild(candidate.LearnerId)"
                                                disabled="@isMutatingChildren">
                                            Add
                                        </button>
                                    </li>
                                }
                            </ul>
                        }
                    }
                </div>
                <div class="modal-footer">
                    <button class="btn btn-secondary" @onclick="CancelEditChildren">Done</button>
                </div>
            </div>
        </div>
    </div>
}
```

### 4. Code-behind — new state and methods

Add these fields near the other modal-state fields:

```csharp
private Dictionary<string, Parent> parentByUserId = new();
private int? parentToEditId;
private string parentToEditName = string.Empty;
private bool isLoadingChildren;
private bool isSearchingChildren;
private bool isMutatingChildren;
private string? editChildrenError;
private List<Learner> linkedChildren = new();
private string childSearchTerm = string.Empty;
private List<Learner>? childSearchResults;
```

Add these methods. Read the comments — they explain *why* each guard exists, not just that it does; keep that reasoning intact if you adapt anything:

```csharp
private async Task ShowEditChildren(int parentId, string fullname)
{
    // Same reentrancy guard as isImporting/isLoadingRisk/isLoadingProfile elsewhere in
    // this app: a double-click fires this handler twice before the first await resolves.
    if (isLoadingChildren) return;

    parentToEditId = parentId;
    parentToEditName = fullname;
    editChildrenError = null;
    childSearchTerm = string.Empty;
    childSearchResults = null;
    isLoadingChildren = true;
    StateHasChanged();

    try
    {
        linkedChildren = await DbContext.ParentLearners
            .AsNoTracking()
            .Where(pl => pl.ParentId == parentId)
            .Include(pl => pl.Learner).ThenInclude(l => l.User)
            .Select(pl => pl.Learner)
            .ToListAsync();
    }
    catch (Exception ex)
    {
        editChildrenError = $"Error loading linked children: {ex.Message}";
        linkedChildren = new();
    }
    finally
    {
        isLoadingChildren = false;
    }
}

private async Task SearchAvailableChildren()
{
    if (isSearchingChildren || parentToEditId == null || currentSchoolId == null) return;

    isSearchingChildren = true;
    editChildrenError = null;
    StateHasChanged();

    try
    {
        var linkedIds = linkedChildren.Select(c => c.LearnerId).ToHashSet();

        var query = DbContext.Learners
            .AsNoTracking()
            .Include(l => l.User)
            .Where(l => l.User.SchoolId == currentSchoolId && !linkedIds.Contains(l.LearnerId));

        if (!string.IsNullOrWhiteSpace(childSearchTerm))
        {
            var term = childSearchTerm.Trim();
            query = query.Where(l => l.User.Fullname.Contains(term));
        }

        childSearchResults = await query.OrderBy(l => l.User.Fullname).Take(25).ToListAsync();
    }
    catch (Exception ex)
    {
        editChildrenError = $"Error searching learners: {ex.Message}";
        childSearchResults = new();
    }
    finally
    {
        isSearchingChildren = false;
    }
}

private async Task AddChild(int learnerId)
{
    if (isMutatingChildren || parentToEditId == null) return;
    isMutatingChildren = true;
    editChildrenError = null;
    StateHasChanged();

    try
    {
        // Re-check server-side, not just trust the on-screen search results — those could
        // be stale if a second admin tab (or a second admin) changed something in between.
        var learner = await DbContext.Learners
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.LearnerId == learnerId);

        if (learner == null || learner.User.SchoolId != currentSchoolId)
        {
            editChildrenError = "That learner is no longer available to link.";
            return;
        }

        var alreadyLinked = await DbContext.ParentLearners
            .AnyAsync(pl => pl.ParentId == parentToEditId && pl.LearnerId == learnerId);

        if (alreadyLinked)
        {
            editChildrenError = $"{learner.User.Fullname} is already linked.";
        }
        else
        {
            // A single insert + single SaveChangesAsync is already atomic — no explicit
            // transaction needed here, unlike the multi-entity Identity+DB flow in account
            // creation.
            DbContext.ParentLearners.Add(new ParentLearner
            {
                ParentId = parentToEditId.Value,
                LearnerId = learnerId,
                CreatedDate = DateTime.UtcNow
            });
            await DbContext.SaveChangesAsync();
            successMessage = $"{learner.User.Fullname} linked to {parentToEditName}.";
        }

        // Reload the linked list and (if a search was already showing) the search results
        // together, so the UI never shows the same learner in both lists at once.
        await ShowEditChildren(parentToEditId.Value, parentToEditName);
        if (childSearchResults != null)
            await SearchAvailableChildren();
    }
    catch (Exception ex)
    {
        editChildrenError = $"Error linking learner: {ex.Message}";
    }
    finally
    {
        isMutatingChildren = false;
    }
}

private async Task RemoveChild(int learnerId)
{
    if (isMutatingChildren || parentToEditId == null) return;
    isMutatingChildren = true;
    editChildrenError = null;
    StateHasChanged();

    try
    {
        var link = await DbContext.ParentLearners
            .FirstOrDefaultAsync(pl => pl.ParentId == parentToEditId && pl.LearnerId == learnerId);

        if (link == null)
        {
            editChildrenError = "That link no longer exists.";
        }
        else
        {
            DbContext.ParentLearners.Remove(link);
            await DbContext.SaveChangesAsync();
            successMessage = "Child unlinked.";
        }

        await ShowEditChildren(parentToEditId.Value, parentToEditName);
        if (childSearchResults != null)
            await SearchAvailableChildren();
    }
    catch (Exception ex)
    {
        editChildrenError = $"Error unlinking learner: {ex.Message}";
    }
    finally
    {
        isMutatingChildren = false;
    }
}

private void CancelEditChildren()
{
    parentToEditId = null;
    parentToEditName = string.Empty;
    editChildrenError = null;
    linkedChildren = new();
    childSearchTerm = string.Empty;
    childSearchResults = null;
}
```

Note that `AddChild`/`RemoveChild` call `ShowEditChildren` again as their reload step — that's fine and not a deadlock: by the time they call it, `isLoadingChildren` is already back to `false`, and `isMutatingChildren` (the guard on the Add/Remove buttons themselves) stays `true` for the whole outer method, correctly keeping those buttons disabled through the reload too.

Removing every linked child from a Parent is allowed — don't add a "must have at least one child" restriction here. `Parent/Dashboard.razor` (Phase 3) already handles zero linked children gracefully with its own empty-state message, so this isn't a new failure mode, just verify it in step 3 below rather than guard against it in code.

No new `@using` directives are needed — `Microsoft.EntityFrameworkCore` and `ARIS1.Models` (which contains `Parent`/`ParentLearner`) are both already imported at the top of this file.

## What NOT to touch

- No changes to any file other than `Components/Pages/Admin/UserManagement.razor`.
- No changes to `CreateUser.razor`, the `Parent/` pages, `SchoolAuthorizationService.cs`, or any model/migration.
- Don't touch the existing Edit Class, Reset Password, Deactivate/Reactivate, or Bulk Import code paths in this file beyond the two additive changes in steps 1–2 above.

## Steps to run, in order

1. Make the changes above.
2. `dotnet build` — confirm it compiles clean.
3. **Live verification on the real running app**, using an existing Parent account from Phase 3 (or create one via `CreateUser.razor` first):
   - Open `/admin/users`, confirm the Parent row now shows an "Edit Children" button and every other row/button is unchanged.
   - Click it — modal opens, shows the currently linked child(ren) with no error.
   - Search for a different, unlinked learner by (partial) name, confirm only learners from your own school appear, click Add — confirm it appears under "Currently Linked" and the success message shows.
   - Click Remove on a linked child — confirm it disappears from "Currently Linked".
   - Remove every linked child from one Parent account, confirm the modal handles zero children cleanly (no crash, "No children linked yet." shown), then log in as that Parent and confirm `/parent/dashboard` shows its existing graceful empty state rather than erroring.
   - **Double-click test:** rapidly double-click "Edit Children" on the same row — confirm no duplicate/overlapping load and no exception.
   - **Double-click test:** rapidly double-click "Add" on the same search result twice — confirm the second click is a no-op (button disabled, or "already linked" message) rather than a crash from a duplicate-key database error.
   - Confirm the existing Edit Class, Reset Password, Deactivate/Reactivate, and Bulk Import features on this same page still all work exactly as before — this phase edits a shared file, so regression-check it.
4. Run `git diff --stat` and confirm the only file listed is `Components/Pages/Admin/UserManagement.razor`.

Report back: confirmation of every bullet in step 3 (pass/fail, not just "looks fine"), and the `git diff --stat` output.
