# Prompt for Claude Code — ARIS Parent Account, Phase 5 (Bulk Import Support)

Copy everything below the line into Claude Code, run from the ARIS1 project root. This assumes Phases 1–4 are already applied — `Models/Parent.cs`, `Models/ParentLearner.cs`, `SchoolAuthorizationService`, the `Parent/` pages, and the "Edit Children" feature in `UserManagement.razor` all already exist. If any of those are missing, stop and tell me instead of improvising them.

I have verified the current, real contents of `Services/BulkUserImportService.cs`, `Services/PasswordGenerator.cs`, and `wwwroot/templates/bulk-user-import-template.csv` before writing this — the code below is written against what's actually in those files today, not a guess. Read them yourself first anyway before editing, to confirm nothing has changed since.

---

## Context

Bulk import currently creates Teacher and Learner accounts from a CSV/Excel file (`BulkUserImportService.ImportAsync`, called from `UserManagement.razor`'s `HandleFileSelected`). This phase adds a third row type: `Parent`, linked to one or more **already-existing** learners via a new `LinkedLearnerEmails` column.

**Hard scope boundary, stated up front: a Parent row may only link to learners that already exist in the database before the import runs.** Do not attempt to resolve a `LinkedLearnerEmails` reference to a Learner row being created earlier in the same file. That would require a two-pass, dependency-ordered import (process all Learner rows first, build an email→LearnerId map, then process Parent rows) — a materially bigger and riskier change than this phase is scoped for. If a referenced email doesn't already exist as a learner in the school, that Parent row fails validation with a clear message, full stop.

## Required changes

### 1. `Services/BulkUserImportService.cs`

Add `using Microsoft.EntityFrameworkCore;` to the usings (needed for the new `FirstOrDefaultAsync` call below).

**`TemplateHeaders`** — append the new column:
```csharp
public static readonly string[] TemplateHeaders = { "FullName", "Email", "Role", "Grade", "ClassName", "Password", "LinkedLearnerEmails" };
```
Before assuming this constant is purely documentation, grep the codebase for other references to `TemplateHeaders` — if something else consumes it, make sure your change doesn't break that caller.

**`ValidateRow`** — extend to accept `Parent` as a role, and validate `LinkedLearnerEmails` format (not existence — that needs a DB call, which happens later in `ImportAsync`, the same separation of concerns the file already uses for the "Email already in use" check). Add a `linkedLearnerEmailsRaw` parameter and this logic:

```csharp
private static string? ValidateRow(string fullName, string email, string role, string gradeRaw, string className, string linkedLearnerEmailsRaw, HashSet<string> seenEmails)
{
    if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2)
        return "Full name is required (min 2 characters).";

    if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        return "A valid email address is required.";

    if (seenEmails.Contains(email))
        return "Duplicate email within the import file.";

    if (!string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(role, "Learner", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(role, "Parent", StringComparison.OrdinalIgnoreCase))
        return "Role must be 'Teacher', 'Learner', or 'Parent'.";

    if (string.Equals(role, "Learner", StringComparison.OrdinalIgnoreCase))
    {
        if (!int.TryParse(gradeRaw, out var grade) || grade < 10 || grade > 12)
            return "Grade must be 10, 11, or 12 for learners.";

        if (string.IsNullOrWhiteSpace(className) || !IsValidClassName(className, gradeRaw))
            return $"Class must be grade {gradeRaw} followed by a single letter, e.g. {gradeRaw}A.";
    }

    if (string.Equals(role, "Parent", StringComparison.OrdinalIgnoreCase))
    {
        var candidateEmails = linkedLearnerEmailsRaw
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (candidateEmails.Length == 0)
            return "LinkedLearnerEmails is required for Parent rows (semicolon-separated learner email(s)).";

        var invalidFormat = candidateEmails.FirstOrDefault(e => !new EmailAddressAttribute().IsValid(e));
        if (invalidFormat != null)
            return $"'{invalidFormat}' in LinkedLearnerEmails is not a valid email address.";

        if (candidateEmails.Length != candidateEmails.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "LinkedLearnerEmails contains a duplicate email within the same row.";
    }

    return null;
}
```

**`ImportAsync`** — three changes to the existing loop, in order:

a) Read the new column and pass it into `ValidateRow`:
```csharp
var linkedLearnerEmailsRaw = row.GetValueOrDefault("LinkedLearnerEmails", string.Empty).Trim();
```
(add this alongside the existing `fullName`/`email`/`role`/etc. reads, and add the parameter to the `ValidateRow(...)` call)

b) Extend `normalizedRole` to three-way:
```csharp
var normalizedRole = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase) ? "Teacher"
    : string.Equals(role, "Parent", StringComparison.OrdinalIgnoreCase) ? "Parent"
    : "Learner";
```

c) Immediately after the existing "Email already in use" check and before the password-generation step, resolve every linked learner email for Parent rows — **all of them, before anything is written**:

```csharp
var linkedLearnerIds = new List<int>();
if (normalizedRole == "Parent")
{
    var candidateEmails = linkedLearnerEmailsRaw
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var unresolved = new List<string>();
    foreach (var candidateEmail in candidateEmails)
    {
        var candidateUser = await _userManager.FindByEmailAsync(candidateEmail);
        var candidateLearner = candidateUser == null
            ? null
            : await _dbContext.Learners.FirstOrDefaultAsync(l => l.UserId == candidateUser.Id);

        if (candidateUser == null || candidateUser.SchoolId != schoolId || candidateLearner == null)
            unresolved.Add(candidateEmail);
        else
            linkedLearnerIds.Add(candidateLearner.LearnerId);
    }

    if (unresolved.Count > 0)
    {
        results.Add(new ImportRowResult(rowNumber, fullName, email, normalizedRole, false,
            $"LinkedLearnerEmails not found as an existing learner in your school: {string.Join(", ", unresolved)}. " +
            "Only learners that already exist before this import can be linked — a parent and their child cannot be created in the same file.",
            null));
        continue;
    }
}
```

This must run — and be able to `continue` past the row on failure — **before** `UserManager.CreateAsync` is called for that row. The point is that a Parent row either fully succeeds (account created, every listed child linked) or creates nothing at all; it must never create a User account and then fail to link some of the children. Note the school check (`candidateUser.SchoolId != schoolId`) — this is the same tenant-isolation rule already enforced in Phase 4's "Edit Children" search; a bulk import must not be able to link a parent to a learner in a different school just because the CSV said so.

Then, in the existing `try` block where `Teacher`/`Learner` entities are created, add the `Parent` branch:

```csharp
if (normalizedRole == "Teacher")
{
    _dbContext.Teachers.Add(new Teacher { UserId = user.Id });
}
else if (normalizedRole == "Parent")
{
    var parent = new Parent { UserId = user.Id };
    foreach (var learnerId in linkedLearnerIds)
    {
        parent.Children.Add(new ParentLearner
        {
            LearnerId = learnerId,
            CreatedDate = DateTime.UtcNow
        });
    }
    _dbContext.Parents.Add(parent);
}
else
{
    _dbContext.Learners.Add(new Learner
    {
        UserId = user.Id,
        Grade = int.Parse(gradeRaw),
        ClassName = className.ToUpperInvariant(),
        EnrollmentYear = DateTime.Now.Year
    });
}
await _dbContext.SaveChangesAsync();
```

A single `_dbContext.Parents.Add(parent)` with the `ParentLearner` rows attached via `parent.Children` and one `SaveChangesAsync()` is enough — EF Core resolves the generated `ParentId` and fixes up the `ParentLearner` foreign keys as part of the same save, no second round trip needed.

**Deliberately not changed:** if `SaveChangesAsync()` throws inside that `try` block for a Parent row (already validated, so this should be rare — a genuine DB-level failure), the existing `catch` reports "Account created but role setup failed" and leaves the `User` row in place, exactly like it already does for Teacher/Learner today. Don't add a new compensating-delete path for Parent alone — that would make Parent rows behave inconsistently with their Teacher/Learner siblings in this same method. This is a pre-existing, accepted limitation of this service, not something to fix in this phase.

### 2. `wwwroot/templates/bulk-user-import-template.csv`

Current content:
```
FullName,Email,Role,Grade,ClassName,Password
Jane Doe,jane.doe@school.com,Teacher,,,
John Smith,john.smith@school.com,Learner,10,10A,
```

Replace with:
```
FullName,Email,Role,Grade,ClassName,Password,LinkedLearnerEmails
Jane Doe,jane.doe@school.com,Teacher,,,,
John Smith,john.smith@school.com,Learner,10,10A,,
Mary Smith,mary.smith@school.com,Parent,,,,john.smith@school.com
```

(The example Parent row links to the example Learner row above it purely for illustration — remember from the scope boundary above that this only works in the real app if `john.smith@school.com` already exists as a learner *before* this file is imported. It's fine as a template illustration since it's just documentation, not something that will actually be imported.)

### 3. `Components/Pages/Admin/UserManagement.razor` — help text only

Update the bulk-import modal's instructional text to describe the new column and role. Find this block:
```razor
<p>Upload a CSV or Excel (.xlsx) file with columns: <code>FullName, Email, Role, Grade, ClassName, Password</code>.</p>
<ul class="small text-muted">
    <li><strong>Role</strong> must be <code>Teacher</code> or <code>Learner</code>.</li>
    <li><strong>Grade</strong> and <strong>ClassName</strong> apply to learners only (e.g. Grade 10, Class 10A).</li>
    <li><strong>Password</strong> is optional — leave blank to auto-generate one (shown in the results below).</li>
</ul>
```
Replace with:
```razor
<p>Upload a CSV or Excel (.xlsx) file with columns: <code>FullName, Email, Role, Grade, ClassName, Password, LinkedLearnerEmails</code>.</p>
<ul class="small text-muted">
    <li><strong>Role</strong> must be <code>Teacher</code>, <code>Learner</code>, or <code>Parent</code>.</li>
    <li><strong>Grade</strong> and <strong>ClassName</strong> apply to learners only (e.g. Grade 10, Class 10A).</li>
    <li><strong>LinkedLearnerEmails</strong> applies to parents only — one or more existing learner emails, separated by semicolons (e.g. <code>john.smith@school.com;jane.smith@school.com</code>). The learner(s) must already exist in your school before this import runs.</li>
    <li><strong>Password</strong> is optional — leave blank to auto-generate one (shown in the results below).</li>
</ul>
```

This is a text-only change — do not touch `HandleFileSelected`, the `isImporting` guard, or anything else in this file. No new buttons or async entry points are being added in this phase; the existing single `InputFile OnChange="HandleFileSelected"` guarded by `if (isImporting) return;` is the only interactive control involved, and it doesn't need to change — the extra per-Parent-row database work inside `ImportAsync` just makes one already-awaited loop take a bit longer, it doesn't introduce any new concurrency surface.

## What NOT to touch

- No changes to `Models/`, `Data/AppDbContext.cs`, `SchoolAuthorizationService.cs`, the `Parent/` pages, or `CreateUser.razor`.
- No changes to `ParseCsv`/`ParseExcel` — the file-format parsing is unaffected, only the row-interpretation logic in `ImportAsync`/`ValidateRow` changes.
- No two-pass or dependency-ordered import logic — see the scope boundary above.
- No compensating-delete logic added for the Parent row's failure path — see the note above.

## Steps to run, in order

1. Make the changes above.
2. `dotnet build` — confirm it compiles clean.
3. **Live verification on the real running app**, via `/admin/users` → Bulk Import:
   - **Backward compatibility:** re-import an old-style file using only the original six columns (no `LinkedLearnerEmails` at all) with a Teacher and a Learner row. Confirm both still import successfully exactly as before — this column must be fully optional for non-Parent rows and absent-column-safe.
   - Import a file with one valid Parent row linking to one real, pre-existing learner in your school. Confirm the Parent account is created, and use Phase 4's "Edit Children" (or log in as that parent) to confirm the link is really there.
   - Import a Parent row listing two valid pre-existing learners separated by `;`. Confirm both get linked.
   - Import a Parent row where `LinkedLearnerEmails` references an email that doesn't exist as a learner at all. Confirm that row fails with a clear error and — check this specifically — that **no User account was created** for that row (query `AspNetUsers` for that email, or try importing it again and confirm it's not rejected as "already in use").
   - Import a Parent row referencing a real learner's email, but one that belongs to a **different school**. Confirm it's rejected the same way — this is the tenant-isolation check, and it must actually be exercised, not just present in the code.
   - Import a Parent row referencing a learner email that only exists in an **earlier row of the same file** (not already in the database beforehand). Confirm it's rejected — this proves the "no same-file dependency resolution" scope boundary is actually enforced, not just documented.
   - In a single file, mix a good Teacher row, a good Learner row, a good Parent row, and a broken Parent row (bad linked email) together. Confirm the three good rows succeed and only the broken one fails — the existing "one bad row fails only that row" behavior must still hold with the new role mixed in.
   - **Rapid-click / overrun check:** while an import with several Parent rows is mid-flight (a slightly larger file helps make this reproducible), try clicking the file input / re-triggering the upload again before it finishes. Confirm the existing `isImporting` guard still blocks the second attempt cleanly — no exception, no duplicate processing, no partial double-import.
4. Run `git diff --stat` and confirm exactly three files changed: `Services/BulkUserImportService.cs`, `wwwroot/templates/bulk-user-import-template.csv`, and `Components/Pages/Admin/UserManagement.razor` (help text only in the last one).

Report back: the result of every bullet in step 3 (pass/fail, not just "looks fine"), and the `git diff --stat` output.
