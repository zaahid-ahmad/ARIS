# ARIS1 Blazor Server — Architectural Analysis
**Date:** 2026-08-05  
**Analyst:** Claude Code (claude-sonnet-4-6)  
**Scope:** All C# entities, configuration, services, and Razor pages/components  
**Last updated:** 2026-09-02 (latest) — Four changes: (1) Teacher Dashboard subject risk-donut cards, (2) a Year (promotion) weighting structure on top of the existing per-term weighting, surfaced as an overall percentage teachers/learners/parents can now actually see, (3) a batched-query performance fix to `RiskAssessmentService` that removed an N+1 pattern making the Teacher Dashboard and AtRisk pages take several seconds to load, and (4) `RiskAssessmentService`'s academic-average input switched from a flat all-time mark average to each learner's own current-term weighted mark (issue #36) — a real per-learner-vs-subject-wide bug in the first version of this fix was caught live during verification and corrected before it shipped. Changes marked ✅ *2026-09-02* below.
**Previously:** 2026-08-27 (latest) — Sign-in no longer honors `ReturnUrl` at all, on any of the three sign-in completion paths (password, 2FA, recovery code) — always lands on the caller's own dashboard now (see issue #29).
**Before that:** 2026-08-27 — Bulk Import extended to create Parent accounts linked to already-existing learners via a new `LinkedLearnerEmails` CSV column. Changes marked ✅ *Parent Bulk Import* below.
**Before that:** 2026-08-27 — Parent Phase 4 (admin can edit a Parent's linked children from `UserManagement.razor`), then a new `SchoolClass` entity replacing free-text `Learner.ClassName` with an admin-managed, per-grade class list (`/admin/classes`) selected via dropdown everywhere a class is assigned. Changes marked ✅ *Parent Phase 4* and ✅ *Class Management* below.
**Previously:** 2026-08-27 (earlier) — New `Parent` role added end-to-end across three phases: DB schema (`Parent`/`ParentLearner`), authorization (`SchoolAuthorizationService` ownership checks), and the Parent-facing UI (Dashboard/Marks/Attendance/Overview) plus admin account-creation support. Changes marked ✅ *Parent role* below.
**Before that:** 2026-08-27 (earlier still) — Teacher At-Risk scatter chart (attendance vs. academic average), Teacher Dashboard alert simplified to a click-through banner, and three double-click/race-condition crashes found and fixed across the app. Changes marked ✅ *2026-08-27* below.
**Before that:** 2026-08-26 — Sprint 3: learner/teacher alert notifications, bulk user import (CSV/Excel), password recovery for admins, bulk subject allocation by class. Changes marked ✅ *Sprint 3*.
**Before that:** 2026-08-15 — UI shell rebuild (`dimpho-frontend.md`) applied; login redirect vulnerability found and fixed

---

## Table of Contents

1. [Project Architecture](#1-project-architecture)
2. [Authentication & Authorization](#2-authentication--authorization)
3. [Dependency Injection](#3-dependency-injection)
4. [Data Access & Database Design](#4-data-access--database-design)
5. [Services Layer](#5-services-layer)
6. [Components & Pages](#6-components--pages)
7. [Areas Needing Improvement](#7-areas-needing-improvement)

---

## 1. Project Architecture

ARIS (Assessment and Reporting Information System) is a **multi-tenant Blazor Server application** for K-12 schools, managing student assessments, attendance, weighted grading, and learner interventions.

**Tech stack:** .NET 10, Blazor Server (Interactive Server render mode), EF Core 10, SQL Server LocalDB, ASP.NET Core Identity.

The multi-tenancy root is the `School` entity. Every user (except `SuperAdmin`) belongs to exactly one school, and `SchoolAuthorizationService` enforces that boundary on all data access.

### Role Hierarchy

| Role | SchoolId | Capabilities |
|---|---|---|
| `SuperAdmin` | `null` | All schools, all admins |
| `Admin` | required | Their school: users, subjects, enrollment |
| `Teacher` | required | Marks entry, attendance, at-risk analysis |
| `Learner` | required | View own marks, attendance, interventions |
| `Parent` | required | ✅ *Parent role* — View marks/attendance/interventions for linked children only, no write access anywhere |

### Domain Model (Conceptual)

```
School (multi-tenancy root)
├── Users (Admin, Teacher, Learner, Parent)
├── Parent ←→ Learner (many-to-many via ParentLearner) — ✅ *Parent role*
└── Subjects
    ├── AssessmentType (e.g., "SBA", "Exam") per term
    │   └── Assessment (individual test instance)
    │       ├── AssessmentQuestion (question-level breakdown)
    │       │   └── LearnerQuestionMark (score per learner per question)
    │       └── LearnerMark (aggregate score per learner)
    ├── LearnerSubject (enrollment many-to-many)
    ├── AttendanceSession → AttendanceRecord (per learner)
    ├── WeightingStructure (per term, or Term==0 for the subject's Year structure — ✅ *2026-09-02*) → WeightingNode (hierarchical tree)
    └── GradeBand (custom APS level lookup per subject)
```

### NuGet Dependencies (`.csproj`)

| Package | Version | Note |
|---|---|---|
| Microsoft.EntityFrameworkCore | 10.0.3 | Primary ORM |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.3 | SQL Server provider |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.3 | Identity integration |
| Microsoft.AspNetCore.Identity.UI | 10.0.2 | Pre-built identity UI |
| EntityFramework | 6.5.1 | ~~Dead weight — EF6 alongside EF Core~~ ✅ Removed |
| ClosedXML | 0.105.1 | ✅ *Sprint 3* — reads `.xlsx`/`.xls` for bulk user import |
| CsvHelper | 33.1.0 | ✅ *Sprint 3* — reads `.csv` for bulk user import |

---

## 2. Authentication & Authorization

### Identity Configuration (`Program.cs` lines 27–46)

- Cookie-based auth via `IdentityConstants.ApplicationScheme`
- Custom `IdentityRevalidatingAuthenticationStateProvider` re-validates the security stamp every **30 minutes** — correct for Blazor Server circuits
- Password policy: 8+ chars, digit required, **no uppercase or special character required** (weak)
- Email confirmation **disabled** (`RequireConfirmedAccount = false`)
- Passkey/WebAuthn endpoints wired in `IdentityComponentsEndpointRouteBuilderExtensions.cs`

### Authorization Model

All pages use `[Authorize(Roles = "...")]` attribute-based role checks only. There are **no custom resource-level policies**. A teacher who knows a subject's ID could reach another school's data unless `SchoolAuthorizationService` is explicitly called inside the component — which is not consistently enforced.

### Identity Endpoints

Standard scaffolded Identity pages exist under `Components/Account/Pages/`:
`Login`, `Register`, `ConfirmEmail`, `ForgotPassword`, `ResetPassword`, `Manage/Index`, `Manage/ChangePassword`, etc.

### Security Observations

| Area | Status |
|---|---|
| HTTPS enforced (`UseHttpsRedirection`) | ✅ |
| HSTS configured | ✅ |
| Antiforgery token on forms | ✅ |
| SQL injection protected (EF parameterization) | ✅ |
| School-based data isolation | ✅ (mostly) |
| Password strength | ❌ No uppercase/special required |
| Email verification | ❌ Disabled |
| Rate limiting on login | ❌ Missing |
| Resource-level authorization policies | ❌ Missing |
| Audit trail | ❌ Missing |
| Hardcoded seed credentials | ❌ `DbSeeder.cs` lines 49, 73 |

---

## 3. Dependency Injection

All services registered in `Program.cs`:

| Service | Lifetime | Responsibility |
|---|---|---|
| `AppDbContext` | Scoped | EF Core unit of work |
| `WeightingService` | Scoped | CRUD + validation of weighting trees |
| `WeightCalculationService` | Scoped | Weighted term mark + APS level calculation; ✅ *2026-09-02* also year (promotion) mark calculation |
| `InterventionService` | Scoped | Creates/updates intervention records after marking |
| `SchoolAuthorizationService` | Scoped | Multi-tenant school access checks |
| `RiskAssessmentService` | Scoped | ✅ *Sprint 3* — shared at-risk scoring, used by `Teacher/AtRisk.razor` and the Teacher Dashboard alerts/donut cards; ✅ *2026-09-02* batched (`CalculateRiskScoresForSubject`) to fix an N+1 query pattern |
| `BulkUserImportService` | Scoped | ✅ *Sprint 3* — parses CSV/Excel and creates Teacher/Learner/Parent accounts in bulk (Parent added ✅ *Parent Bulk Import*) |
| `BulkSubjectAllocationService` | Scoped | ✅ *Sprint 3* — previews and commits class-wide subject enrollment |

`PasswordGenerator` (✅ *Sprint 3*) is a stateless static utility, not DI-registered — shared by `BulkUserImportService` and the admin Reset Password flow to avoid duplicating the random-password logic.

### Issues

- All services are **concrete class registrations** — no interfaces (`IWeightingService`, etc.). Unit testing and mocking are impossible without refactoring.
- Services take `AppDbContext` directly — no repository or unit-of-work abstraction layer.
- No `ILogger<T>` injection in any service.

### Configuration — ✅ *Sprint 3*

`Program.cs` now sets `HubOptions.MaximumReceiveMessageSize` to 10 MB (default is 32 KB). Blazor Server's `InputFile` streams uploads over the SignalR circuit, so the default limit rejected most real CSV/Excel files before the bulk-import feature could parse them.

---

## 4. Data Access & Database Design

`AppDbContext` extends `IdentityDbContext<User>` and owns 16 entity sets.

### Cascade Delete Strategy

| Relationship | Behavior | Reason |
|---|---|---|
| Most FK relationships | `Restrict` | Avoids SQL Server cascade path errors |
| `AssessmentQuestion → Assessment` | `Cascade` | Questions deleted with assessment |
| `LearnerQuestionMark → Question` | `Cascade` | Marks cleaned up with question |
| `WeightingNode` (self-ref parent) | `NoAction` | Prevents cascade in hierarchies |

### Key Indexes

| Table | Index | Type |
|---|---|---|
| `School` | `Code` | Unique |
| `WeightingStructure` | `(SubjectId, Term)` | Composite unique |
| `GradeBand` | `(SubjectId, MinPercentage, MaxPercentage)` | Composite, for range queries |

### Indexes

EF Core's FK convention auto-generates an index for every foreign key column. All `LearnerId`, `AssessmentId`, and related FK columns are indexed via the `InitialCreate` migration (`IX_LearnerMarks_LearnerId`, `IX_LearnerQuestionMarks_LearnerId`, `IX_AttendanceRecords_LearnerId`, `IX_Interventions_LearnerId`, etc.). No manual index additions are required.

### Entity-Level Issues

| Entity | Issue |
|---|---|
| `User` | `Role` string property duplicates `IdentityRole` — can drift out of sync |
| `Assessment`, `LearnerMark`, `AssessmentType` | ~~`MaxMark`/`MarksAwarded`/`WeightPercentage` stored as `float` — rounding errors in grade calculations~~ ✅ Migrated to `decimal(10,4)` |
| `AttendanceRecord` | `Status` is `string` ("Present", "Absent", "Late", "Excused") — should be `enum` |
| `Intervention` | `Level` is `string` — should be `enum`; `CreatedDate` uses `DateTime.Now` (local time) |
| `WeightingNode`, `WeightingStructure`, `GradeBand` | `CreatedDate`/`LastModifiedDate` use `DateTime.Now` — should use `DateTime.UtcNow` |
| `LearnerMark` | No `CreatedDate`/`ModifiedDate` at all |
| `Parent`, `ParentLearner` | ✅ *Parent role* — new. `Parent` wraps `User` 1:1 (same pattern as `Teacher`/`Learner`); `ParentLearner` is a composite-key (`ParentId`, `LearnerId`) many-to-many join, same pattern as `LearnerSubject`. `Parent → User` FK is `Cascade` (unspecified, matches `Teacher`/`Learner` convention); both `ParentLearner` FKs are `Restrict`, per the project's default |
| `Learner.ClassName` (string) | ✅ *Class Management* — **removed.** Replaced with `Learner.ClassId` (required FK to new `SchoolClass`). See §6 `Admin/ClassManagement.razor` for the full design and the migration's backfill logic |
| `SchoolClass` | ✅ *Class Management* — new. `(SchoolId, Grade, Name)` unique together; `Grade` is a plain `int` (10/11/12, not an FK — matches `Learner.Grade`'s existing convention, not normalized further). `SchoolClass → School` and `Learner → SchoolClass` are both `Restrict`, so a class with learners still assigned cannot be deleted at the DB level (also pre-checked in the UI for a friendlier message) |
| `WeightingNode` | ✅ *2026-09-02* — new nullable `ReferencedTerm` (`int?`) column (migration `AddYearWeighting`), set only when `NodeType == "Term"`; says which term's weighted mark that node substitutes into a Year structure. `WeightingStructure.Term == 0` is a reserved sentinel (never a real term) for "this is the subject's Year structure" — fits the existing unique `(SubjectId, Term)` index with no index change. **Gotcha found while building this:** `WeightingStructure.RootNodes` is the raw EF collection navigation for the `WeightingStructureId` FK — i.e. *every* node belonging to the structure, not just top-level ones. The original flat per-term editor never had children so this never mattered; the Year editor does, and both the admin page's load and `WeightCalculationService.CalculateYearMark` had to explicitly filter to `ParentNodeId == null` to get true roots. |

### Database Seeding (`Data/DbSeeder.cs`)

Runs on every startup via `db.Database.Migrate()` in `Program.cs`:

1. Creates four default roles
2. Creates `Default School` if none exist
3. Creates `superadmin@aris.com` / `SuperAdmin@1234`
4. Creates `admin@aris.com` / `Admin@1234`

**Issues:** Credentials hardcoded in source (`DbSeeder.cs` lines 49, 73). `context.Schools.First()` at line 79 will throw if school creation failed. No error handling or logging.

---

## 5. Services Layer

### 5.1 `WeightingService` (`Services/WeightingService.cs`, 154 lines)

**Responsibilities:** CRUD for `WeightingStructure` and `WeightingNode`, hierarchy validation, flat weighting creation.

**Positive:** Uses `AsNoTracking()` on reads (line 45). Floating-point tolerance in validation is 0.01% (correct).

**Issues:**

| Location | Issue |
|---|---|
| Lines 24–25 | Throws generic `Exception` on missing subject — no custom exception type |
| Lines 126–128 | Loads all nodes into memory to delete them individually — should be a batch delete |
| Validation methods | Synchronous; no `ILogger` |

---

### 5.2 `WeightCalculationService` (`Services/WeightCalculationService.cs`, 266 lines)

**Responsibilities:** Compute a learner's weighted term percentage, map to APS level (0–7), break down by assessment type.

**Key method:** `CalculateWeightedTermMark(int learnerId, int subjectId, int term) → WeightedTermResult`

**✅ *2026-09-02* — `CalculateYearMark(int learnerId, int subjectId) → YearMarkResult` added.** Prior to this, `CalculateWeightedTermMark` was never called from any page — it was registered in DI and otherwise dead code; every mark-facing page instead computed its own flat, unweighted average across every mark (ignoring both assessment-type weighting and term boundaries). `CalculateYearMark` evaluates a subject's Year-level `WeightingStructure` (`Term == 0`) recursively: a `"Term"` node calls the existing `CalculateWeightedTermMark` for that term; an `"AssessmentType"` node (e.g. a Final Exam) computes that one type's percentage standalone via a new private `CalculateAssessmentTypePercentage` helper, independent of any term's own weighting structure; a `"Custom"` group node just recurses into its children, with each node's weighting interpreted relative to its parent (same convention `WeightingService.ValidateWeightingStructure` already uses). Almost no new percentage math — mostly orchestration on top of the existing method. Now actually wired into `Teacher/LearnerProfiles.razor`, `Learner/Marks.razor`, and `Parent/Marks.razor` (see §6).

**✅ *2026-09-02* — `CalculateWeightedTermMarksForSubject(subjectId, term, learnerIds) → Dictionary<int, WeightedTermResult>` added**, batched counterpart to `CalculateWeightedTermMark` (a handful of DB queries for the whole subject/term instead of ~3 per learner). Added specifically so `RiskAssessmentService.CalculateRiskScoresForSubject` (§5.5) could source its academic-average input from a real weighted term mark without reintroducing the N+1 pattern just fixed there. The per-type weighted-sum math common to both the single-learner and batched methods was extracted into a shared private `BuildWeightedTermResult` helper so they can't drift apart, same pattern as `RiskAssessmentService`'s `BuildRiskData`. The batched path also loads `GradeBand`s once per subject and looks them up in memory (`GetApsLevelFromBands`) rather than one blocking query per learner — the single-learner `CalculateWeightedTermMark` still calls the pre-existing synchronous `GetAPSLevel` per call (issue below, not touched here).

**Issues:**

| Location | Issue |
|---|---|
| Line 51–55 | No `Include()` for related data — potential N+1 |
| Line 136 | `GetAPSLevel()` makes a **synchronous blocking database call** |
| Line 136 | No `AsNoTracking()` on GradeBand query |
| Line 212 | `150%` mark-validation multiplier is a hardcoded magic number |
| Lines 122–126 | `catch` block swallows exception without logging |
| All calculations | `float` arithmetic — should be `decimal` |

---

### 5.3 `SchoolAuthorizationService` (`Services/SchoolAuthorizationService.cs`, 64 lines)

**Responsibilities:** Multi-tenant access checks by `SchoolId`.

**Issues:**

| Location | Issue |
|---|---|
| Lines 39, 57 | No null checks — throws unhandled exception if user or subject not found |
| All methods | No caching — hits the database on every call |
| Overall | Only checks school membership, not teacher-subject or learner-subject assignment |

**✅ *Parent role* — `GetAccessibleLearnerIds(string? userName)` and `HasAccessToLearner(string? userName, int learnerId)` added.** Both are deny-by-default (return empty/`false`, never throw) and resolve the calling user → `Parent` → `ParentLearners` → learner-id set, entirely independent of `SchoolId` (a parent's access is defined by the explicit link, not school membership). Used by all four Parent pages: `Dashboard.razor` calls `GetAccessibleLearnerIds` to filter the children list; `Marks.razor`/`Attendance.razor`/`Overview.razor` each call `HasAccessToLearner` inside `OnParametersSetAsync` (not `OnInitializedAsync`) so that a route-parameter change on an already-live component re-validates ownership rather than trusting whatever check ran when the component was first constructed. Verified live: tampering the URL from an authorized learner's id to a real, unlinked learner's id on all three per-child pages correctly showed "You do not have access to this learner's data." with no data leakage.

---

### 5.4 `InterventionService` (`Services/InterventionService.cs`, 98 lines)

**Responsibilities:** Creates or updates an `Intervention` record after each question is marked.

**Thresholds:**

| Range | Level | Message |
|---|---|---|
| ≤ 30% | Critical | "Critical attention in {topic}" |
| 31–55% | Attention | "Attention needed in {topic}" |
| 56–65% | Focus | "Focus on {topic}" |
| 66–79% | Minor | "Minor improvements needed in {topic}" |
| ≥ 80% | WellDone | "Well done in {topic}" |

**Issues:**

| Location | Issue | Status |
|---|---|---|
| Line 18 | Division by zero if `maxMark = 0` | ✅ Fixed |
| Lines 22–24 | Silent `return` if question not found — should log and throw | Open |
| Lines 63–66 | Upsert check is not atomic — concurrent saves can create duplicates | Open |
| Line 74 | Updates `CreatedDate` on re-save — should use a separate `ModifiedDate` | Open |
| Thresholds | Hardcoded — should be configurable | Open |

---

### 5.5 `RiskAssessmentService` (`Services/RiskAssessmentService.cs`) — ✅ *Sprint 3*

**Responsibilities:** Computes a learner's composite risk score (60% academics / 30% attendance / 10% trend) and level (Critical ≥55, High ≥45, Moderate ≥30, Low otherwise) for one subject.

Extracted from `Teacher/AtRisk.razor`, which previously had this exact algorithm as a private method. It now backs two call sites — the original `AtRisk.razor` page and a notification banner on `Teacher/Dashboard.razor` that checks across *all* of a teacher's subjects without requiring one to be selected first. Extracting it was necessary once a second caller needed the identical thresholds; keeping two copies would have let them drift apart silently.

**Same pre-existing issues as before the extraction (not addressed by the refactor):** no `ILogger`, thresholds still hardcoded, trend factor still a fixed `0.5`.

**✅ *2026-09-02* — N+1 query pattern fixed.** `CalculateRiskScore(learnerId, subjectId)` made 2 sequential DB queries (attendance, marks) *per learner*; any page walking every learner in a subject (Teacher Dashboard's per-subject risk-donut cards, `AtRisk.razor`'s scatter chart/table) was therefore making 2×(learners×subjects) round trips — for a teacher with a few compulsory-subject classes, several hundred sequential round trips before the page could render, reported live as "several seconds" of delay just navigating to the Dashboard. Fixed by adding `CalculateRiskScoresForSubject(subjectId, learnerIds) → Dictionary<int, RiskData>`: 2 batched queries (`WHERE LearnerId IN (...)`, grouped in memory) for the whole subject instead of 2 per learner. The scoring formula itself was extracted into a shared private `BuildRiskData` helper so the single-learner and batched paths can never drift apart — same output, not a behavior change. `Teacher/Dashboard.razor` and `Teacher/AtRisk.razor` both switched to the batched call. Verified live: Riverside's Bongani Van Wyk (3 Mathematics sections × 84 learners) — Dashboard numbers unchanged (90 at-risk, identical per-subject Critical/High/Moderate/Low counts) before and after, and the batched `IN`-clause queries execute in 1–9ms each per the server's EF Core command log, versus ~500 sequential round trips before.

**✅ *2026-09-02* — Academic average switched from a flat all-time mark average to each learner's own current-term weighted mark (issue #36, below).** `AcademicAverage` previously averaged every `LearnerMark` ever recorded for the learner in the subject with equal weight and no term boundary — no assessment-type weighting, absences silently excluded rather than penalized, and old terms diluting a recent decline (or recent improvement). Fixed by sourcing it from `WeightCalculationService`: each learner's **current term** (the highest term for which *that learner specifically* has a recorded mark) is resolved, then `CalculateWeightedTermMark`/the new batched `CalculateWeightedTermMarksForSubject` (§5.2) supplies the type-weighted percentage for that term if a `WeightingStructure` is configured there, else a simple average of just that term's marks (never blended across the subject's whole history) so scoring keeps working before an admin has set weighting up. `BuildRiskData` simplified accordingly — it now takes an already-resolved `AcademicAverage` decimal rather than a raw marks list, going back to pure formula application. Attendance (30%) and the trend factor (10%, still fixed at 0.5) were deliberately not touched.

**A real bug was caught live during verification, not just a test artifact.** The first version resolved "current term" **subject-wide** (`MAX(Assessment.Term)` across the whole subject). Testing it by adding one Term 4 mark for a single learner made **83 of the other 84 learners** in that class collapse to a 0% academic average and "Critical" — because they had no marks yet in what had just become "the" current term, even though they had perfectly good Term 3 marks one step back. This would have hit real usage the moment any teacher created a new term's assessment before finishing marking the whole class for it. Fixed by resolving "current term" **per learner** instead — grouping learners by their own most-recently-marked term and batching each group, typically 1–2 distinct terms per subject in practice, never one query per learner. Re-verified after the fix: Mathematics G10's risk distribution returned to a sensible spread (18/13/27/26 Critical/High/Moderate/Low, not 83 Critical), the Term-4 fallback case correctly showed 50.0% for the one learner who actually had a Term 4 mark, and the rest of the class was unaffected.

Verified live end-to-end against Riverside's seeded data: Riaan Van Wyk (Mathematics G10) moved from the old flat-average 70.2% to his correct weighted Term 3 mark, 64.1% — matching the number already shown on `Teacher/LearnerProfiles.razor` and `Learner/Marks.razor` for the same learner/subject, resolving the visible inconsistency issue #36 originally flagged.

### 5.6 `BulkUserImportService` (`Services/BulkUserImportService.cs`) — ✅ *Sprint 3*

**Responsibilities:** Parses a `.csv` or `.xlsx`/`.xls` file into a common `List<Dictionary<string,string>>` row structure (CsvHelper for CSV, ClosedXML for Excel), validates each row with the same rules as `CreateUser.razor` (name length, email format, role, grade/class for learners, `LinkedLearnerEmails` for parents — ✅ *Parent Bulk Import*), then creates each `User`/`Teacher`/`Learner`/`Parent` via `UserManager`/`AppDbContext` — one row at a time, since Identity's `CreateAsync` isn't batchable.

**Key behaviors:**
- A blank `Password` column auto-generates a policy-compliant password via `PasswordGenerator`; a supplied password is validated by Identity as normal.
- One bad row (invalid email, wrong role, duplicate email) fails only that row — the rest of the batch still processes — reported back as `ImportRowResult` per row.
- Wired into `Admin/UserManagement.razor` via a modal with a downloadable CSV template.

**Issues:** No `ILogger`. Generated passwords are only ever held in memory for the results view — by design, nothing persists them — but that means there is no way to recover one if the admin navigates away before copying it (see the CSV-download and Reset-Password-Generate mitigations below).

**✅ *Parent Bulk Import* — `Parent` is now a third importable role**, linked via a `LinkedLearnerEmails` column (semicolon-separated learner emails). Deliberately single-pass, not dependency-ordered: a Parent row can only link to a learner that **already exists in the database at the moment that row is processed** — either from before the import started, or from an earlier row in the *same* file (each row commits via its own `SaveChangesAsync()`, so an earlier row's Learner is already durable by the time a later row's Parent resolution query runs). A Parent row referencing a learner listed *later* in the same file fails, by design — resolving that would need a two-pass, dependency-ordered import, which is explicitly out of scope. All resolution (existence + same-school tenant check) happens before `UserManager.CreateAsync` runs for that row, so a rejected Parent row never creates an orphaned `User`. Verified live: backward compatibility with the old six-column format (no `LinkedLearnerEmails` column at all), single- and multi-learner links, a nonexistent-learner rejection, a different-school-learner rejection (worded identically to the nonexistent case, so a bulk import can't be used to probe which emails exist in another school), a true forward-reference rejection (Parent row before its Learner row), a mixed good/bad file (three succeed, one fails, independently), and the pre-existing `isImporting` guard still preventing any duplicate/corrupted state across two overlapping upload attempts of the same file.

**✅ *Class Management* — `ClassName` column now resolved against real classes, not free-typed.** `ResolveClassAsync(schoolId, grade, className)` looks up an existing `SchoolClass` by exact name match first, then — for backward compatibility with spreadsheets written before this feature — retries with the grade-digit prefix stripped (`"10A"` under Grade 10 → tries `"A"`). No match fails that row with `"No class '{className}' exists for Grade {grade} at this school. Create it under Class Management first."` rather than silently creating an orphaned class. Verified live with a 3-row CSV: an existing short-form class, a nonexistent class (correctly rejected with that message), and the legacy long-form exact match (correctly resolved) — all three outcomes matched expectations in one run.

### 5.7 `BulkSubjectAllocationService` (`Services/BulkSubjectAllocationService.cs`) — ✅ *Sprint 3*

**Responsibilities:** Bulk-enrolls every learner in a Grade+Class into one or more subjects at once, via `Admin/BulkSubjectAllocation.razor` (`/admin/bulk-enrollment`).

**Design points, called out because this was the trickiest of the Sprint 3 additions:**
- **Grade integrity** — subjects are filtered to the selected grade in the query itself, and re-validated (count-matched) before any write, so a stale or tampered subject-ID list can never cross-enroll a learner into a subject from another grade.
- **Idempotency** — existing `(LearnerId, SubjectId)` pairs are computed fresh on every Preview *and* every Allocate call and simply skipped, never re-inserted (would otherwise violate `LearnerSubject`'s composite PK and abort the whole batch on the first collision — the single-learner `EnrollLearner` in `LearnerEnrollment.razor` still has this exact gap, since it doesn't pre-check before inserting).
- **AcademicYear** — a required, visible, editable field (default: current year), explicitly *not* silently defaulted. Because `LearnerSubject`'s key is `(LearnerId, SubjectId)` only — it does **not** include `AcademicYear`, so a learner can only ever have one row per subject — an already-enrolled pair is skipped outright rather than having its `AcademicYear` overwritten. This is a real schema constraint (confirmed against `AppDbContext`'s fluent config), not a choice made for this feature, and it means the app cannot represent "enrolled in Math in 2025, re-enrolled in Math in 2026" as two rows.
- **Blast radius** — nothing is written until an explicit second "Confirm" click on a review screen (learner count, subject count, new-vs-skipped counts, academic year, expandable learner list).
- **Batching** — one `BeginTransactionAsync` + single `AddRange`/`SaveChangesAsync`, matching the fix already applied to `Teacher/Marks.razor` and `Teacher/Attendance.razor` rather than reintroducing their original N-round-trip pattern.

**Issues:** No `ILogger`. A genuinely concurrent second admin action between a Preview and its matching Confirm click is not guarded against (Allocate re-checks existing pairs at commit time, which covers the common sequential case but not a race).

**✅ *Class Management* — `GetClassNamesAsync` now queries `SchoolClasses` directly** instead of `SELECT DISTINCT ClassName FROM Learners` (a class with zero learners currently enrolled now still appears in the dropdown, which is arguably more correct — it's a real, admin-created class, not just an artifact of who happens to be assigned to it today). `LoadAndValidateAsync`'s learner filter changed from `l.ClassName == className` to `l.Class.Name == className`; the service's public signature (grade + `string className`) is unchanged, so `BulkSubjectAllocation.razor` needed no changes at all.

## 6. Components & Pages

### Page Inventory

| Area | File | Route | Auth |
|---|---|---|---|
| Root | `Home.razor` | `/` | Any |
| SuperAdmin | `SuperAdmin/Dashboard.razor` | `/superadmin/dashboard` | SuperAdmin |
| SuperAdmin | `SuperAdmin/Schools.razor` | `/superadmin/schools` | SuperAdmin |
| SuperAdmin | `SuperAdmin/Admins.razor` | `/superadmin/admins` | SuperAdmin |
| Admin | `Admin/Dashboard.razor` | `/admin/dashboard` | Admin |
| Admin | `Admin/UserManagement.razor` | `/admin/users` | Admin |
| Admin | `Admin/SubjectManagement.razor` | `/admin/subjects` | Admin |
| Admin | `Admin/ClassManagement.razor` | `/admin/classes` | Admin — ✅ *Class Management* |
| Admin | `Admin/LearnerEnrollment.razor` | `/admin/enrollment` | Admin |
| Admin | `Admin/BulkSubjectAllocation.razor` | `/admin/bulk-enrollment` | Admin — ✅ *Sprint 3* |
| Teacher | `Teacher/Dashboard.razor` | `/teacher/dashboard` | Teacher |
| Teacher | `Teacher/Marks.razor` | `/teacher/marks` | Teacher |
| Teacher | `Teacher/Attendance.razor` | `/teacher/attendance` | Teacher |
| Admin | `Admin/AttendanceManagement.razor` | `/admin/attendance` | Admin |
| Teacher | `Teacher/AtRisk.razor` | `/teacher/atrisk` | Teacher |
| Teacher | `Teacher/LearnerProfiles.razor` | `/teacher/learnerprofiles` | Teacher |
| Learner | `Learner/Dashboard.razor` | `/learner/dashboard` | Learner |
| Learner | `Learner/Marks.razor` | `/learner/marks` | Learner |
| Learner | `Learner/Attendance.razor` | `/learner/attendance` | Learner |
| Learner | `Learner/Profile.razor` | `/learner/profile` | Learner |
| Learner | `Learner/Support.razor` | `/learner/support` | Learner |
| Parent | `Parent/Dashboard.razor` | `/parent/dashboard` | Parent — ✅ *Parent role* |
| Parent | `Parent/Marks.razor` | `/parent/marks/{LearnerId}` | Parent — ✅ *Parent role* |
| Parent | `Parent/Attendance.razor` | `/parent/attendance/{LearnerId}` | Parent — ✅ *Parent role* |
| Parent | `Parent/Overview.razor` | `/parent/overview/{LearnerId}` | Parent — ✅ *Parent role* |

### Detailed Page Issues

#### `Home.razor`
- Redirects unknown roles to `/access-denied` — ✅ Fixed (`Components/Pages/AccessDenied.razor` created at route `/access-denied`).

#### `Admin/CreateUser.razor` — ✅ Per-field validation added
- Per-field error strings (`fullNameError`, `emailError`, `passwordError`, `roleError`, `gradeError`) replace the single `errorMessage` banner.
- `ValidateForm()` runs before submit: required checks, min 2-char name, email format (`EmailAddressAttribute`), password min 8 chars + digit, role/grade selection.
- Duplicate-email check surfaces as a field-level error on the email input (not a generic banner).
- `is-invalid` + `invalid-feedback` divs per field; errors clear as user types (`@oninput`).
- Password requirements hint shown below the password field.
- **Class assignment added** ✅ — `Learner.ClassName` was previously hardcoded to `"Unassigned"` at creation with no field to set it. A required "Class" text input now appears when role = Learner, validated by `IsValidClassName(className, grade)`: must be exactly the selected grade's digits followed by one letter A–Z (e.g. `10A`), case-insensitive input normalized to uppercase on save (`ToUpperInvariant()`). Mismatch shows `classNameError` inline: `"Class must be grade {grade} followed by a single letter, e.g. {grade}A."`.
- **Free-text Class replaced with a Grade → Class dropdown pair** ✅ *Class Management* — superseding the entry directly above. Selecting a Grade reloads a `<select>` of that grade's `SchoolClass` rows (`OnGradeChanged` → `LoadGradeClasses`); if none exist yet, an inline hint links to `/admin/classes`. Server-side re-validates the selected `ClassId` belongs to the admin's own school + grade before creating the `Learner`, same pattern as the existing Parent learner-selection re-validation just below it in this file.

#### `Admin/SubjectManagement.razor` (God component)
- Covers six distinct concerns: subject CRUD, assessment type CRUD, assessment CRUD, question CRUD, weighting config, and weighting validation — all in one file.
- 40+ component-level state fields.
- Multiple `SaveChangesAsync()` calls in `LoadWeightingStructure()` without a wrapping transaction.
- Loads all subjects and teachers into memory on `OnInitializedAsync`.
- **Manage modal redesigned** ✅ — replaced single vertical-scroll wall with two tabs (Assessments / Weightings) sharing a term pill selector. Assessment types each get their own card; assessments expand inline to show questions. Weightings tab auto-loads on switch — no manual Load button. `ConfirmDelete` now re-fetches via `FindAsync` before removing to fix the optimistic concurrency error.
- **Per-field validation added** ✅ — Create/Edit subject modals: `subjectNameError`, `teacherIdError`, `yearError` / `editNameError`, `editTeacherError`, `editYearError`; year range enforced to 2020–(current year + 1). Inline assessment-type form: highlights empty name input with `is-invalid`. Inline assessment form: `newTitleInvalid`, `newAMaxMarkInvalid` (max mark > 0 now validated), `newDateInvalid`. Inline question form: `newTopicInvalid`, `newQMarkInvalid` (max mark > 0 enforced). All inline booleans reset when the question panel is toggled.
- **Weighting input trailing-zeros bug fixed** ✅ — EF Core reads the `decimal(10,4)` SQL Server column and preserves the stored scale, so `0` came back as `0.0000m` and `ToString()` produced `"0.0000"` in the number input. `@bind:format` was attempted but rejected by the Blazor compiler (only valid for DateTime types). Fixed with explicit `value="@FormatWeight(node.Weighting)"` + `@onchange` using a `FormatWeight` helper (`"0.##"` format: strips trailing zeros, no decimal point for whole numbers). `OnWeightChanged` parses with invariant culture and clamps to 0–100. `step` changed from `"0.1"` to `"any"` so the browser does not reject fractional entries like `33.33`. Total row and validation error message also updated to `"0.##"` format.
- **✅ *2026-09-02* — "Year Weighting" tab added**, third tab alongside Assessments/Weightings, independent of the shared Term selector (Year isn't term-scoped). Builds a subject's Year `WeightingStructure` (`Term == 0`) as an arbitrary tree — not hardcoded to any fixed SBA/exam shape — two levels deep in practice: root nodes each typed as Group/Term/Assessment Type, Group roots can have Term/Assessment-Type children. Add/remove root nodes and children, live "must be 100%" validation at both the root level and per-group children level, reusing `WeightingService.ValidateWeightingStructure` directly against the in-memory tree rather than duplicating validation logic. Save is clear-and-recreate (delete all existing nodes for the structure, rebuild from the edited tree) rather than diffing — same precedent already used by `ConfirmDeleteAssessmentType` and `WeightingService.CreateSimpleWeighting`, and simpler than diffing an arbitrary add/remove/reparent tree.
  - **Two real bugs found and fixed during verification** (not hypothetical — caught by building a structure through the UI and checking the DB): (1) `WeightingStructure.RootNodes` is the raw FK-collection navigation, not a true-roots-only filter — see §4 entity table. (2) New "Term" child nodes had no default `ReferencedTerm`, so the `<select>` displayed "Term 1" (the browser's default-first-option behavior for an unbound `<select>`) while the underlying model value stayed `null` — nothing wrote it back unless the admin happened to touch that specific dropdown. Fixed with a sensible default on node creation (`AvailableTerms.FirstOrDefault()`) plus a save-time coalesce as a safety net for nodes whose type changed after creation.
  - **Grade 12 → Terms 1–3 only.** The "Shared Term Selector" (`new[] {1,2,3,4}`) is now a computed `AvailableTerms` property returning `{1,2,3}` when `selectedSubject.Grade == 12`, used by both the Assessments/Weightings tabs' term pills and the Year tab's Term-node term picker. Server-side guard in `CreateAssessmentType`/`CreateAssessment` rejects Term 4 for Grade 12 subjects even if `manageTerm` were somehow set to 4 by other means — the real NSC final exam mark is held by the DBE, not captured in ARIS, so Term 4 doesn't exist in this system for Grade 12 at all.
  - Verified live end-to-end against Riverside's seeded data: built a real SBA(25%: Term1 60%/Term2 40%) + Final Exam(75%, referencing the Term 3 Exam `AssessmentType`) structure for Mathematics Grade 10, hand-computed the expected result from the actual seeded marks, and confirmed the app produced exactly that (Term 1 49.7%, Term 2 62.4%, Year Mark 77.2%) — identically on the teacher's Learner Profile view and the learner's own Marks page. Confirmed Grade 12 subjects can't reference or create Term 4 anywhere in either tab.

#### `Admin/ClassManagement.razor` — ✅ *Class Management* new page
- New page at `/admin/classes`: three cards (Grade 10/11/12), each listing that grade's `SchoolClass` rows with a live learner count, inline Rename, inline Add, and Delete-with-confirmation (blocked with a friendly message — "Cannot delete a class that still has learners assigned to it" — when the count is nonzero, ahead of the FK's own `Restrict` constraint).
- Rename/Add/Delete each guarded by a single shared `isProcessing` flag (same reentrancy-guard house style as `UserManagement.razor`'s `isImporting`/`isLoadingChildren`), and each duplicate-name check is case-insensitive and scoped to `(SchoolId, Grade)`.
- **Razor gotcha hit while building this:** the per-grade "add class" text input sits inside a `@foreach (var grade in Grades)` loop and needs its bound value keyed by `grade`. Binding directly to a `Dictionary<int,string>` indexer (`@bind="newClassNames[grade]"`) compiled and *looked* correct, but silently failed to round-trip keystrokes back to the server at all — the backing dictionary entry never updated, with no exception anywhere. Confirmed via direct DOM-level event dispatch (bypassing simulated typing entirely) that the failure was in the binding itself, not the test method. Fixed by dropping the dictionary indexer for three explicit per-grade fields (`newClassName10`/`11`/`12`) addressed through `@bind:get`/`@bind:set` accessor methods instead — the same `@bind:event="oninput"` pattern used successfully elsewhere on this page (the Rename input) works fine once the bind target is a plain field rather than a collection indexer.
- Verified live end-to-end: add (including the duplicate-name rejection), rename (both the change and confirming the DB write via a fresh load), delete blocked while in use, delete succeeding once the class is empty, and the resulting dropdown/table effects on `CreateUser.razor`, `UserManagement.razor`'s Edit Class modal, `BulkUserImportService`, and `BulkSubjectAllocationService` (§5.6, §5.7) — all cross-checked against the real dev database.

#### `Admin/UserManagement.razor` (310 lines)
- Hardcoded string comparison `u.Role != "Admin"` (line 193) prevents admins from managing other admins — may or may not be intentional.
- No pagination on the user list.
- **Reset Password modal validation added** ✅ — `passwordResetError` shown inline below the password field; validates required, min 8 chars, at least one digit before hitting Identity. Password requirements hint added. `CancelReset` now clears `passwordResetError`.
- **Browser autofill bug fixed** ✅ — Opening the Reset Password modal caused Chrome/Edge to scan the page for the nearest text input to pair with the `<input type="password">`, finding the search box and filling it with saved credentials (e.g. `admin@aris.com`). This fired `@bind`'s `onchange`, setting `searchTerm` to the autofilled value and filtering the users list to empty. Fixed with `autocomplete="new-password"` on the password field (suppresses credential-fill even when Chrome ignores `autocomplete="off"`) and `autocomplete="off"` on the search input. Same fix applied to `CreateUser.razor`.
- **Modal state decoupled from users list** ✅ — `User? userToReset` (a reference to an EF Core–tracked entity from the `users` list) replaced with three primitives: `userToResetId`, `userToResetName`, `userToResetEmail`. `ConfirmResetPassword` now does a fresh `UserManager.FindByIdAsync` lookup. This eliminates potential Blazor render-tree conflicts from having the same tracked object in two roles simultaneously.
- **Edit Class added** ✅ — new "Class" column (learners only, "-" for teachers) plus an "Edit Class" button opening a modal, so existing `"Unassigned"` learners (and any others) can be corrected after creation. `LoadUsers()` batch-loads `learnerByUserId` (`Dictionary<string, Learner>` keyed by `UserId`) alongside the existing role lookup. `ShowEditClass(learnerId, fullname, grade, currentClassName)` captures the learner's actual `Grade` so `ConfirmEditClass()` can validate with the same `IsValidClassName(value, grade)` rule as `CreateUser.razor` (grade digits + one letter, e.g. `10A`); modal title and placeholder display the grade for context. `ConfirmEditClass` re-fetches via `DbContext.Learners.FindAsync` before writing, saves uppercased, and refreshes the list.
- **Bulk Import added** ✅ *Sprint 3* — "Bulk Import" button opens a modal accepting `.csv`/`.xlsx`/`.xls` (columns: `FullName, Email, Role, Grade, ClassName, Password`), backed by `BulkUserImportService` (§5.6). Per-row results table shows Created/Failed with the failure reason; a "Download credentials (CSV)" link appears when any row got an auto-generated password, since it's the only chance to capture it (see Reset Password below). Tested end-to-end in-browser against real `.csv` and `.xlsx` files, including a 60-row file with a role-terminology mismatch (`"Student"` vs the app's `"Learner"`) that the validation correctly rejected rather than silently coercing.
- **Reset Password: Generate + Show** ✅ *Sprint 3* — the existing Reset Password modal gained a "Generate" button (fills the field with a `PasswordGenerator`-produced password) and a "Show password" checkbox, so an admin can also recover from a lost bulk-import password later by issuing and reading back a fresh one for that specific user, without inventing one by hand against the password policy.
- **Bulk-import re-entrancy guard added** ✅ *2026-08-27* — `HandleFileSelected` previously relied only on `disabled="@isImporting"` on the `<InputFile>` to stop a second upload while one was processing (UI-only, not a code-level guard) — see issue #34. Added `if (isImporting) return;` at the top of the handler. Verified live: uploaded two different CSVs back-to-back with no delay — both imports completed correctly and sequentially with no crash and no data loss, confirming the guard prevents overlapping `DbContext` use without blocking legitimate consecutive imports.
- **Edit Children added for Parent rows** ✅ *Parent Phase 4* — a Parent row now gets an "Edit Children" button opening a modal to add/remove linked `ParentLearner`s, following the exact structural pattern of the existing Edit Class modal: `ShowEditChildren`/`SearchAvailableChildren`/`AddChild`/`RemoveChild`/`CancelEditChildren`, each with the same `isLoadingChildren`/`isSearchingChildren`/`isMutatingChildren` reentrancy guards as `isImporting` above. `AddChild` re-validates the learner's school server-side before inserting (never trusts the search results alone). Removing every linked child is allowed by design — `Parent/Dashboard.razor` already has its own empty-state message. Verified live including both double-click tests (rapid double-click on "Edit Children" and on "Add" for the same learner) — no duplicate load, no duplicate-key crash.
- **Edit Class replaced with a Grade-scoped dropdown** ✅ *Class Management* — the free-text `editClassName` input (validated by the same `IsValidClassName` regex as `CreateUser.razor`) is now a `<select>` populated from `SchoolClasses` for that learner's grade, loaded fresh each time the modal opens (`ShowEditClass`). `ConfirmEditClass` re-validates the selected class server-side before writing `Learner.ClassId`.

#### `SuperAdmin/Schools.razor` — ✅ Per-field validation added
- Per-field errors: `nameError`, `codeError` (enhanced), `emailError`, `phoneError`.
- `ValidateSchoolForm()` checks: name required + min 2 chars; email format validated if non-empty; phone max 20 chars.
- `ValidateCodeUniqueness()` enhanced: now also checks min 3 chars and `^[A-Za-z0-9\-]+$` pattern before the DB uniqueness query.
- All fields get `is-invalid` + `invalid-feedback` divs; modal re-open resets all error strings to `null`.

#### `SuperAdmin/Admins.razor` — ✅ Per-field validation added
- Per-field errors: `fullnameError`, `adminEmailError`, `schoolError` replace the single `modalError` banner.
- `ValidateAdminForm()` checks: fullname required + min 2 chars, email required + format, school selected (non-zero).
- Duplicate-email check from Identity now surfaces as `adminEmailError` on the email field.
- `ShowAddAdminModal()` resets all three error strings to `null` on open.

#### `Teacher/Marks.razor`
- N+1 query pattern — ✅ Fixed (batch queries applied to `LoadQuestions` and `SaveMarks`).
- Three separate `SaveChangesAsync()` calls with no transaction boundary — ✅ Fixed (`BeginTransactionAsync` / single `SaveChangesAsync` / `CommitAsync`).
- Mark capture restructured as a modal — ✅ Fixed (assessment table opens modal-xl per assessment; modal must be closed before switching; auto-save with 800 ms debounce per learner; green "✓ Saved" badge persists until Done/close).
- Subject-switching bug (stale assessments visible before Load) — ✅ Fixed (`@bind:after="OnSubjectChanged"` clears stale state immediately).
- Mark inputs displayed raw `decimal` trailing zeros (e.g. `5.0000`) — ✅ Fixed (`"0.##"` format strips trailing zeros; whole numbers display as integers).
- Entering a mark above the question max clamped silently to the max — ✅ Fixed (field clears, red `is-invalid` border + `Max: X` hint shown; nothing saved until corrected).

#### `Teacher/Attendance.razor` — ✅ Fully rewritten
- Duplicate sessions created on every save — ✅ Fixed (upsert: checks subject + date + time before creating)
- Two `SaveChangesAsync` with no transaction — ✅ Fixed (`BeginTransactionAsync` wraps entire save)
- No subject ownership check — ✅ Fixed (verifies `selectedSubjectId` is in teacher's own subject list)
- No way to view or edit past sessions — ✅ Fixed (last 10 sessions listed; Edit button pre-populates the form)
- All learners defaulted to "Present" — ✅ Fixed (dropdown starts with `-- Select --`; red border on unset; save blocked until all set)
- Future dates allowed — ✅ Fixed (`max` attribute on date input; server-side validation in `LoadLearners` and `SaveAttendance`)
- Raw string literals for status values — ✅ Fixed (inner `AttendanceStatus` static class with `const string` fields)
- Multiple sessions per day — ✅ Added (`TimeOnly Time` column on `AttendanceSession`; timestamp captured automatically when "Load Learners" is clicked; duplicate check includes time)

#### `Admin/LearnerEnrollment.razor` — ✅ *Sprint 3* addition
- Unchanged otherwise — still one Grade+Subject at a time via a two-panel Enrolled/Available list with per-row Enroll/Remove buttons.
- New "Bulk Allocate by Class" button links to `/admin/bulk-enrollment` for the class-wide case.
- `EnrollLearner` still does not check for an existing `(LearnerId, SubjectId)` row before inserting — relies on the composite-key violation throwing, caught by the page's generic `try/catch`, surfaced as a raw exception message rather than a friendly "already enrolled" error. `BulkSubjectAllocationService` (§5.7) does this check properly; this single-learner path was not touched.

#### `Admin/BulkSubjectAllocation.razor` — ✅ *Sprint 3* new page
- New page at `/admin/bulk-enrollment`; see §5.7 (`BulkSubjectAllocationService`) for the grade-integrity/idempotency/blast-radius/batching design.
- Two-phase UI: Grade → Class → Subjects (checkboxes) → Academic Year → "Preview Allocation" builds a `ClassAllocationPreview`; a second "Confirm" click is required to actually write, with the Confirm button disabled outright when the preview has zero new enrollments.
- Verified live: grade-integrity (switching grade correctly swaps the subject checkbox list, never bleeding a same-named class's subjects across grades), idempotency (re-running an identical allocation reports 0 new / N skipped instead of erroring), and the transaction/`AddRange` commit path, against the real dev database with rollback afterward.

#### `Admin/AttendanceManagement.razor` — ✅ New page
- Admin-only page at `/admin/attendance` scoped to the admin's school.
- Lists all attendance sessions with filters by grade, subject, and date range.
- Inline expand/collapse to view each session's learner records with colour-coded status badges.
- Delete with inline confirmation; deletes records before session (Restrict FK); wrapped in transaction.
- Dashboard card added to `/admin/dashboard`.
- `filteredSessions` refactored from template `@{ }` local variable to a C# computed property `FilteredSessions` to support pagination and `@bind:after` filter resets.

#### `Components/Shared/Pager.razor` — ✅ New shared component
- Reusable Bootstrap pagination component. Parameters: `CurrentPage` (int), `TotalPages` (int), `OnPageChanged` (EventCallback<int>).
- Renders prev/next buttons and numbered page buttons. Smart windowing: all pages shown if ≤ 7, otherwise first / current±1 / last with `…` separators.
- Renders nothing when `TotalPages ≤ 1` — zero overhead on small lists.
- `@using ARIS1.Components.Shared` added to `Components/_Imports.razor` — available globally without per-page imports.
- Build issue resolved: `@page` inside button text was mis-parsed as a Razor directive; fixed with `@(page)` explicit expression syntax.

#### Pagination — ✅ Applied to all list pages (`PageSize = 15`)

| Page | List paginated | Page reset on |
|---|---|---|
| `SuperAdmin/Schools.razor` | `schools` | `LoadSchools()` |
| `SuperAdmin/Admins.razor` | `admins` | `LoadData()` |
| `Admin/UserManagement.razor` | `FilteredUsers` (search-filtered) | `SearchUsers()`, `ClearSearch()` |
| `Admin/LearnerEnrollment.razor` | `enrolledLearners` + `availableLearners` (two independent pagers) | `LoadEnrollments()` resets both |
| `Admin/AttendanceManagement.razor` | `FilteredSessions` (filter-computed property) | Grade/subject/date filter changes, `ClearDateFilter()`, after session delete |
| `Admin/SubjectManagement.razor` | `filteredSubjects` | `LoadData()`, `OnGradeChanged()` |
| `Teacher/Marks.razor` | `assessments` | `OnSubjectChanged()` |
| `Teacher/Attendance.razor` | `pastSessions` (removed previous `Take(10)` limit — all sessions now visible) | `LoadPastSessions()` |
| `Teacher/AtRisk.razor` | `atRiskLearners` | `LoadAtRiskLearners()` |
| `Teacher/LearnerProfiles.razor` | `filteredLearners` | `SearchLearners()`, `ClearSearch()` |
| `Learner/Marks.razor` | `subjects` (paginated over subject cards) | On load |
| `Learner/Attendance.razor` | `subjects` (paginated over subject cards) | On load |

Pattern used in each page:
```csharp
private int currentPage = 1;
private const int PageSize = 15;
private int totalPages => (int)Math.Ceiling(list.Count / (double)PageSize);
private List<T> PagedItems => list.Skip((currentPage - 1) * PageSize).Take(PageSize).ToList();
private void GoToPage(int page) => currentPage = page;
```

#### `Teacher/AtRisk.razor` (262 lines)
- Loads all attendance records into memory, then counts in C# (lines 209–213).
- Risk score weights (60/30/10) hardcoded — ✅ *Sprint 3* — extracted into `RiskAssessmentService` (§5.5) so a second caller (Teacher Dashboard, below) can't drift out of sync with these thresholds. Values themselves are unchanged and still hardcoded within the service.
- Trend factor always `0.5` — no real trend analysis.
- **Risk Overview scatter chart added** ✅ *2026-08-27* — a hand-rolled inline-SVG scatter plot (no charting library, no JS interop) above the existing at-risk table: one dot per learner in the loaded subject (the *whole* class, not just the flagged ones), X = attendance %, Y = academic average %, both from the same `RiskAssessmentService` call the table already uses. Colored by risk level using the design method's fixed **status** palette (Critical `#d03b3b` / High `#ec835a` / Moderate `#fab219` / Low `#0ca30c`) rather than an invented categorical palette — chosen deliberately, since risk level is an ordered state (good→critical), not series identity; the categorical validator was run anyway as a sanity check and failed exactly the checks the palette's own docs say are expected for status colors (sub-3:1 contrast for two of the four steps), mitigated by never relying on hue alone: a text-labeled legend, a hover/focus tooltip, and a collapsible full-roster table all carry the same information. Hover works via Blazor `@onmouseenter`/`@onfocus` state (no JS) — the dot's screen position is already known server-side, so the tooltip is positioned with plain CSS percentages, no pointer-tracking needed.
  - **Razor/SVG gotcha hit while building this:** a bare `<text>` element as the first markup inside a `@foreach` code block collides with Razor's own reserved `<text>` transition tag (compiler error RZ1023, "cannot contain attributes") — happens only for `<text>` immediately inside a loop body, not for one used as plain sibling markup elsewhere on the same page. Worked around by rendering those two elements via `MarkupString` instead of inline markup.
  - Verified live against real data (`TestTeacher` fixture, Mathematics Grade 10): dots' positions and colors cross-checked pixel-for-pixel against the at-risk table's numbers; hover tooltip and the collapsible table both confirmed correct.
- **✅ *2026-09-02* — N+1 query fix.** `LoadAtRiskLearners`'s per-learner `RiskAssessmentService.CalculateRiskScore` loop (once per enrolled learner in the selected subject) replaced with the batched `RiskAssessmentService.CalculateRiskScoresForSubject` (§5.5) — 2 DB round trips for the whole subject instead of 2 per learner. Same numbers at the time this landed, faster load for subjects with many enrolled learners — a later same-day change (§5.5's academic-average fix, issue #36) did subsequently change the numbers themselves, for correctness reasons unrelated to this batching.

#### `Teacher/Dashboard.razor` — ✅ *Sprint 3* At-Risk notification added, ✅ *2026-08-27* simplified, ✅ *2026-09-02* per-subject risk donuts + perf fix
- On load, walks every subject the teacher teaches and scores every enrolled learner via `RiskAssessmentService` (§5.5) to get a **count** of distinct at-risk learners (score ≥ 45, same cutoff `AtRisk.razor` uses).
- **Originally shipped (2026-08-26) as a full results table** (learner, subject, level, score, attendance %, average, a "View Profile" button per row, with pagination) — mirroring `AtRisk.razor`'s table almost exactly. **Simplified same sprint, 2026-08-27,** per feedback that a dashboard shouldn't dump the whole flagged list: it's now a single click-through notification — a red (`alert-danger`) banner reading "⚠ You have N at-risk learner(s) across your subjects," clickable anywhere on the banner, navigating to `/teacher/atrisk` for the actual detail. Zero at-risk learners still shows the green "Great job!" success state. The `AtRiskAlert` list class, pagination, and `ViewLearnerProfile` helper from the table version were removed; the code-behind now tracks a single `int atRiskLearnerCount` instead of a `List<AtRiskAlert>`.
- **✅ *2026-09-02* — Subject risk-donut cards added.** A responsive card grid below the at-risk banner, one card per subject the teacher teaches: an inline-SVG donut (stroke-dasharray technique stacking one `<circle>` per band on a base ring — no charting library, no JS interop, matching the rest of the app) showing that subject's enrolled-learner count split by Critical/High/Moderate/Low, plus a text legend (colored dot + level name + count — no-hue-alone rule, same as `AtRisk.razor`'s scatter chart) and total-learner count centered in the ring. Zero-enrollment subjects render an empty gray ring with a "No learners enrolled" caption instead of dividing by zero. The existing per-subject risk-walk loop that already computed `atRiskLearnerCount` was extended to *also* accumulate each subject's band counts in the same pass — not a second loop over the data, mirroring why `RiskAssessmentService` itself was extracted in the first place. **Razor/SVG gotcha hit here too** (same class of issue as `AtRisk.razor`'s, §"Teacher/AtRisk.razor" below): avoided by keeping the donut's `<text>`-equivalent (the center count, the legend) as sibling markup after the SVG block rather than the first element inside the `@foreach` drawing the band arcs.
- **✅ *2026-09-02* — N+1 query fix.** The per-learner `RiskAssessmentService.CalculateRiskScore` calls in this loop were replaced with the batched `CalculateRiskScoresForSubject` (§5.5) — this is what the "recomputes risk for every learner × every subject on every dashboard load" trade-off noted below used to cost several seconds for a teacher with large classes; same numbers at the time this landed, ~2 DB round trips per subject instead of ~2 per learner. A later same-day change (§5.5's academic-average fix, issue #36) subsequently changed the risk numbers themselves — visible on this exact donut grid, screenshotted before/after during that fix's own verification.
- Remaining trade-off: still recomputes on every dashboard load, no caching across page loads (unlike `AtRisk.razor`, which only computes on-demand after a subject is explicitly selected). The N+1 problem is fixed; a genuinely cached/precomputed version would need a background job or a short-lived cache, not attempted here.

#### `Teacher/LearnerProfiles.razor` — ✅ *2026-08-27* re-entrancy bug fixed
- `ViewProfile` (bound to each row's "View Profile" button) is called with a `Learner` and clears/repopulates `learnerSubjects`, `learnerInterventions`, and `assessmentMarksCache` across several `await`s with no guard against being invoked again mid-flight — see issue #33. Fixed with an `isLoadingProfile` guard plus building the three fields into locals and assigning them together at the end. `selectedLearner` itself is still set immediately (opens the modal right away, unchanged) since a single scalar assignment isn't part of the race.
- Verified live: two rapid "View Profile" clicks on two different learners (TestStudent then TestStudent3, no delay) — the second was correctly dropped by the guard, modal showed fully consistent data for the first learner only; confirmed the guard resets properly by then viewing the second learner on their own right after.
- **✅ *2026-09-02* — Overall percentage summary added.** Previously this page showed nothing but a raw per-assessment table — no where for a teacher to see a learner's overall percentage in a subject. Each subject's card now opens with an "Overall: Term 1: X% · Term 2: Y% · ... · Year Mark: Z%" line, calling `WeightCalculationService.CalculateWeightedTermMark`/`CalculateYearMark` per subject in `ViewProfile` alongside the existing marks/interventions loading.

#### `Learner/Dashboard.razor` (227 lines)
- Loads **all** interventions into memory (lines 205–213), then groups/filters in C#.
- `ThenBy(i => i.Level)` at line 212 is a string sort — "Attention" sorts before "Critical" alphabetically, not by severity.
- **Alerts section added** ✅ *Sprint 3* — new "Alerts" panel between the quick-action cards and "My Interventions", grouped by subject then by severity (Critical → Attention → Focus, in that fixed order via `AlertLevels` array + `Array.IndexOf` sort — sidesteps the same string-sort trap noted above). Subjects with only Minor/WellDone interventions don't appear — this is an alerts panel, not a full status view. The existing per-intervention badge-color ternary (previously only in the term-by-term modal) was factored into a shared `GetLevelColors(level)` helper, reused by both the new panel and the modal, rather than tripling the same `Critical/Attention/Focus/Minor` → color mapping.

#### `Learner/Marks.razor` — ✅ *2026-09-02* weighted marks replace flat average
- Previously computed a flat, unweighted average across every mark in a subject (`subjectMarks.Average(...)`) — ignored both assessment-type weighting and term boundaries, blending every term/type equally. Replaced with per-term weighted marks + a Year Mark (`Term 1: X% · Term 2: Y% · ... · Year Mark: Z%`, same badge style as the Teacher/LearnerProfiles.razor addition above), via `WeightCalculationService.CalculateWeightedTermMark`/`CalculateYearMark` computed alongside the existing per-assessment mark loading. The raw per-assessment table is unchanged.

#### `Learner/Support.razor` (310 lines) — ✅ Chatbot backed by real learner data
- The "AI Learning Assistant" previously returned one of five hardcoded random responses via `new Random().Next(5)`, regardless of what was typed — misleading to label as AI.
- **Fixed** ✅ — introduced `IChatAssistantService` (`Services/IChatAssistantService.cs`) with a single `GetResponseAsync(userInput, concerns)` method and a `ChatConcern(Subject, Level, Topics)` record, registered in `Program.cs`. `RuleBasedChatAssistantService` (`Services/RuleBasedChatAssistantService.cs`) is the current implementation: it matches the learner's typed message against their own `Intervention` subjects/topics and returns a tip tailored to that concern's severity level (`Critical`/`Attention`/`Focus`/`Minor`/`WellDone`); greetings get a list of the learner's current subjects; anything unmatched gets "I can only help with your current subjects: X, Y — try asking about one of those." No external API, no cost.
- `Support.razor`'s `ConcernItem` now tracks `Topics` (previously only a topic *count*) so the matcher has real topic names to check against.
- Designed for a future upgrade: swapping in a real LLM (e.g. Claude API) later is a one-line DI change in `Program.cs` — implement `IChatAssistantService` against the API, scoped to the learner's `concerns` list, and `Support.razor` needs no changes.

#### `Parent/Dashboard.razor`, `Marks.razor`, `Attendance.razor`, `Overview.razor` — ✅ *Parent role* new pages
- `Dashboard.razor` (`/parent/dashboard`) lists the parent's linked children as cards (name, grade/class) with buttons to each child's Overview/Marks/Attendance; resolves the list via `SchoolAuthorizationService.GetAccessibleLearnerIds` in `OnInitializedAsync` (safe here — no route parameter to re-validate).
- `Marks.razor` and `Attendance.razor` are near-verbatim ports of `Learner/Marks.razor`/`Learner/Attendance.razor`'s data-loading and markup, scoped by route `{LearnerId}` instead of the logged-in user's own id. `Marks.razor` got the same ✅ *2026-09-02* weighted-marks treatment as its Learner-side counterpart (see above) — flat average replaced with per-term weighted marks + Year Mark.
- `Overview.razor` ports `Learner/Dashboard.razor`'s "Alerts" panel (subject → severity grouping, `Critical`/`Attention`/`Focus` only, same fixed-order sort) rather than the full dashboard — `GetLevelColors` is duplicated locally rather than shared, since `Learner/Dashboard.razor` was off-limits to touch in this phase.
- **Razor gotcha hit while building these**: `[Parameter] public int LearnerId { get; set; }` written outside `@code { }` — directly in the markup/directive section, immediately after the `@inject` lines — is not a real property; Razor treats it as literal text, so every reference to `LearnerId` elsewhere in the file (markup or `@code`) fails with `CS0103: The name 'LearnerId' does not exist in the current context`. All three files had this bug (from the build spec's own sample code) and needed the parameter moved inside `@code { }`. `Overview.razor` additionally hit `RZ1010: Unexpected "{" after "@" character` from an `@{ var x = ...; }` block written inside an already-open `else { }` branch — once inside a code block, a nested `@{ }` transition is redundant/invalid; fixed by dropping the `@{ }` wrapper and writing the statement directly (same pattern the working `Learner/Dashboard.razor` uses, but that one's block sits at the top markup level, not nested inside an `if/else` chain).
- Verified live end-to-end against the real dev DB: admin-side account creation (with a duplicate-email attempt correctly rejected pre-creation, so no orphaned `User` row), Dashboard showing exactly the linked children, Marks/Attendance/Overview matching the equivalent Learner-side data, and the URL-tampering test (editing a per-child page's URL to a real, unlinked learner id) correctly blocked on all three pages.
- `Components/Layout/NavMenu.razor`'s new `Parent` section reuses the existing `.nav-section-label` class — inherits the same pre-existing washed-out-text issue as every other role's section label (`rgba(255,255,255,0.25)` text color barely visible against the light sidebar background), not a regression introduced by this change.

#### `NavMenu.razor` / `MainLayout.razor` / `Login.razor` — ✅ UI shell rebuilt (`dimpho-frontend.md`)
- Previously: username only shown for `SuperAdmin` (old `NavMenu.razor` line 24) — all other roles saw no identity indicator; default Blazor template chrome (`Components/Layout/MainLayout.razor`'s `<div class="page"><div class="sidebar">`, Bootstrap `bi-*` icon nav, stock floating-label Login form).
- **Fixed** ✅ — applied the full rebuild guide in `dimpho-frontend.md`, written to route around an earlier "Not Found" incident caused by adding `@rendermode="InteractiveServer"` directly to `Routes`/`MainLayout` (that render mode is deliberately *not* present on either file; the 19 individual pages that already declared it per-page were left untouched).
  - `wwwroot/aris-styles.css` (~2,570 lines) — new custom design system (CSS variables, login screen, sidebar/nav, cards, tables, modals, badges, tabs, stat tiles) loaded globally from `App.razor`, alongside Google Fonts (DM Sans/DM Mono) and Font Awesome 6.4.0 from CDN.
  - `Components/Account/Pages/Login.razor` — rebuilt to the card/gradient design (`.login-wrap`/`.login-card`); added `.field-error` style (missing from the guide's snippet) for per-field `ValidationMessage`s.
  - `Components/Layout/MainLayout.razor` + new `MainLayout.razor.css` — fixed white sidebar shell; collapse-to-icons and the profile panel are plain `onclick` + vanilla JS (not Blazor `@onclick`/C# state) so they work with zero server round-trip and can't trigger the render-mode conflict; collapsed state persisted via `localStorage`, restored on both normal load and Blazor's `enhancedload` event.
  - `Components/Layout/NavMenu.razor` — rewritten to Font Awesome icons with active-route highlighting (`.nav-item.active` gets a blue border); stale `NavMenu.razor.css` (old Bootstrap `bi-*` icon CSS) deleted as dead weight.
  - Header avatar (initials, role-colored) + dropdown now show the display name and role label for **every** role, not just SuperAdmin — resolves the issue above. Name comes from a `Fullname` claim (see `AppUserClaimsPrincipalFactory` below) with a fallback to `Identity.Name`.
  - Duplicate in-body `<h2>` page titles removed from all 18 pages that had one (header now shows the page title); `Learner/Dashboard.razor`'s personalized `<h2>Welcome, @currentUser?.Fullname</h2>` intentionally left alone.
- **`Services/AppUserClaimsPrincipalFactory.cs`** — new `UserClaimsPrincipalFactory<User, IdentityRole>` override that adds `Fullname` and `SchoolId` as claims at sign-in, so `MainLayout.razor` can read the display name/role straight off the `ClaimsPrincipal` instead of a DB call per render. Registered via `.AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()` in `Program.cs`'s Identity builder chain (was initially added as a file with no registration — a gap caught and closed before it shipped). `SchoolAuthorizationService` still does its own `UserManager` DB lookup for `SchoolId` rather than reading the claim — not yet migrated, still a DB round trip per authorization check.

---

## 7. Areas Needing Improvement

### Priority 1 — Critical (correctness / data integrity)

| # | Issue | Status |
|---|---|---|
| 1 | N+1 queries in `Teacher/Marks.razor` (`LoadQuestions` + `SaveMarks`) | ✅ Fixed |
| 2 | `InterventionService` division by zero if `maxMark = 0` | ✅ Fixed |
| 3 | No transaction boundary wrapping the three-phase save in `Teacher/Marks.razor` | ✅ Fixed |
| 4 | Missing database indexes on `LearnerId` (5 tables) | N/A — EF Core FK convention auto-created these in `InitialCreate` migration |  ✅ No fix necessary
| 5 | `float` used for marks and weights — switch to `decimal` | ✅ Fixed |
| 32 | `Teacher/AtRisk.razor`'s `LoadAtRiskLearners` had no guard against being invoked twice concurrently (e.g. double-clicking "Load Learners") — two overlapping runs interleaved their `await`s and cleared/repopulated the shared `learnerRiskCache` dictionary out of sync with `allLearnersInSubject`, producing an unhandled `KeyNotFoundException` that crashed the Blazor circuit (reported live: "hit this when I clicked on the load button twice") | ✅ Fixed *2026-08-27* — added an `isLoadingRisk` guard (`if (isLoadingRisk) return;`); the button now disables itself and reads "Loading…" while a load is in flight. The method also now builds the learner list, risk cache, and at-risk filter into local variables and assigns all three shared fields together in one block at the end, so a render can never observe one field reflecting a new load while another still reflects a stale or concurrent one |
| 33 | `Teacher/LearnerProfiles.razor`'s `ViewProfile` had the identical shape (clear-then-loop-with-awaits repopulating `assessmentMarksCache`, no re-entrancy guard) — found by auditing the rest of the app for the same pattern after fixing #32. Didn't actually crash, since the render already used `ContainsKey` with a fallback instead of a bare indexer, but two rapid "View Profile" clicks on two different learners could show one learner's header alongside a stale/mixed marks cache | ✅ Fixed *2026-08-27* — same pattern: `isLoadingProfile` guard, `learnerSubjects`/`learnerInterventions`/`assessmentMarksCache` built into locals and assigned together |
| 34 | `Admin/UserManagement.razor`'s bulk-import `HandleFileSelected` only disabled the file input in the UI (`disabled="@isImporting"`) with no code-level guard — two overlapping calls could share the same scoped `DbContext` for concurrent async operations, which EF Core does not support and can throw its own exception | ✅ Fixed *2026-08-27* — added `if (isImporting) return;` at the very top of the handler, before any state is touched |

### Priority 2 — High (security / reliability)

| # | Issue | Status |
|---|---|---|
| 6 | Hardcoded seed credentials in `DbSeeder.cs` lines 49, 73 | ✅ Fixed |
| 7 | Dual role storage (`User.Role` string + `IdentityRole`) | ✅ Fixed |
| 8 | No service interfaces — prevents unit testing | Open |
| 9 | No resource-level authorization policies | ✅ Fixed |
| 10 | Weak password policy — no uppercase/special required | ✅ Fixed |
| 11 | `SchoolAuthorizationService` missing null checks (lines 39, 57) | ✅ Fixed |
| 29 | `Login.razor` honored `ReturnUrl` unconditionally after sign-in. `Routes.razor`'s `<RedirectToLogin />` captures the *current page* as `ReturnUrl` on any unauthorized hit (wrong role, not just logged-out) — e.g. a SuperAdmin signing out from `/superadmin/dashboard` bakes that URL into the login link. The next person to sign in through that link (e.g. an Admin) was blindly redirected to `/superadmin/dashboard`, which their role can't access — surfaced as a broken/"Not Found" landing instead of their own dashboard | ✅ Fixed, then **superseded** — originally fixed by checking `ReturnUrl` against the signed-in user's role for four role-scoped prefixes before honoring it (`IsReturnUrlAllowedAsync`); per explicit product decision, this was replaced outright rather than kept as a fallback path — sign-in now **always** lands on `/` (→ the caller's own dashboard via `Home.razor`), full stop, no `ReturnUrl` case survives. Removed from `Login.razor` (`IsReturnUrlAllowedAsync`, `RoleScopedPrefixes`, the `ReturnUrl` query parameter) and, on the same pass, from `LoginWith2fa.razor` and `LoginWithRecoveryCode.razor` — both of those completed a sign-in by blindly honoring whatever `ReturnUrl` was in the query string with **no role check at all**, a gap the original fix never actually covered since it only touched the password path. Verified live: hitting a protected admin page while logged out captures `?ReturnUrl=%2Fadmin%2Fsubjects` on the login redirect as expected; signing in through that exact link lands on `/admin/dashboard`, not `/admin/subjects`. The 2FA/recovery-code paths were fixed identically but not live-verified (no test account has 2FA enabled) — same one-line change as the already-verified password path, low risk |

### Priority 3 — Medium (code quality / scalability)

| # | Issue | Status |
|---|---|---|
| 12 | Per-field inline validation on all user-input pages | ✅ Fixed — `is-invalid` + `invalid-feedback` pattern applied to `CreateUser`, `Schools`, `Admins`, `UserManagement` (password reset), and `SubjectManagement` (create/edit modals + all inline forms); field-level errors replace single `errorMessage` banners; errors clear as user types |
| 12b | String enums — `AttendanceRecord.Status`, `Intervention.Level` | Open |
| 13 | `DateTime.Now` → `DateTime.UtcNow` across all models | Open |
| 14 | No `ILogger<T>` in any service | Open |
| 15 | `SubjectManagement.razor` (1345 lines) needs splitting into child components | Open |
| 16 | No pagination on any list page | ✅ Fixed — `Pager.razor` shared component; applied to all 12 list pages |
| 17 | `/access-denied` route missing — `Home.razor` navigates there for unknown roles | ✅ Fixed |
| 18 | Legacy `EntityFramework 6.5.1` package in `.csproj` alongside EF Core 10 | ✅ Fixed — removed from `ARIS1.csproj`; no code referenced `System.Data.Entity` |
| 19 | `WeightCalculationService.GetAPSLevel()` synchronous blocking DB call (line 136) | Open |
| 28 | `Learner.ClassName` hardcoded to `"Unassigned"` on creation with no UI to ever set/change it — shows "Unassigned" on all learner-facing pages | ✅ Fixed — `CreateUser.razor` and `UserManagement.razor` (see below) |
| 30 | `wwwroot/aris-styles.css` is linked in `App.razor` as a bare `href="aris-styles.css"` instead of `@Assets["aris-styles.css"]` like the other local stylesheets — no cache-busting fingerprint, so a deploy that changes this file can serve a stale cached copy until the browser cache naturally expires. `dimpho-frontend.md` documents this as a known tradeoff (hard-refresh after every change) rather than fixing it | Open |
| 31 | `Admin/LearnerEnrollment.razor`'s single-learner `EnrollLearner` has no duplicate-enrollment guard before insert — relies on the `LearnerSubject` composite-key violation throwing, surfaced as a raw exception message. `BulkSubjectAllocationService` (§5.7, added Sprint 3) does this check properly for the class-wide path; the single-learner path was left as-is | Open |
| 36 | `RiskAssessmentService.CalculateRiskScore`'s `AcademicAverage` used a flat/unweighted average across every mark ever recorded, no term boundary — a different number from the weighted Term/Year marks shown elsewhere (`Teacher/LearnerProfiles.razor`, `Learner/Marks.razor`, `Parent/Marks.razor`), and biased (no assessment-type weighting, old terms diluting recent decline, absences excluded rather than penalized) | ✅ Fixed *2026-09-02* — see §5.5. Sourced from each learner's own current-term weighted mark (`WeightCalculationService`) instead, with a term-scoped simple-average fallback when no `WeightingStructure` is configured yet. The 60/30/10 formula shape itself is unchanged. A real bug was caught live during verification — the first version resolved "current term" subject-wide, which collapsed 83 of 84 classmates to Critical the moment one learner had an early mark in a new term — fixed by resolving it per learner instead; see §5.5 for the full account |

### Priority 4 — Long term

| # | Issue | Status |
|---|---|---|
| 20 | No caching for frequently read, rarely changed data (grade bands, assessment types); Sprint 3's Teacher Dashboard At-Risk Alerts adds another instance — recomputes every learner × subject risk score on every page load | ✅ *2026-09-02* Partially Fixed — the N+1 query pattern (2 DB round trips per learner) is fixed via `RiskAssessmentService.CalculateRiskScoresForSubject` batching (§5.5); still recomputes from scratch on every page load, no cross-request caching or background precomputation |
| 21 | No soft deletes — physical deletes prevent history | Open |
| 22 | No audit trail (`CreatedBy`, `ModifiedBy`, `ModifiedDate`) | Open |
| 23 | No concurrency tokens — simultaneous mark entry can clobber data | Open |
| 24 | Chatbot in `Learner/Support.razor` is fake — random canned responses | ✅ Fixed — `IChatAssistantService` abstraction + rule-based implementation matching real `Intervention` data; free today, swappable for a real LLM later |
| 25 | No bulk import/export (CSV/Excel for marks, enrollment) | ✅ *Sprint 3* Partially Fixed — bulk **user** import (CSV/Excel, `BulkUserImportService`) and bulk **subject enrollment by class** (`BulkSubjectAllocationService`) both added. Marks and attendance still have no bulk import/export path |
| 26 | No report generation (PDF/Excel mark sheets, term reports) | Open |
| 27 | Unused second connection string (`ARIS1Context`) in `appsettings.json` | ✅ Fixed — *Parent role* prep work found two unregistered scaffold `DbContext` classes (`ARIS1Context`, `ARIS_PrototypeContext`, both empty, never referenced in `Program.cs`, no migrations) causing `dotnet ef migrations add` to require `--context AppDbContext`; both deleted along with the orphaned `ARIS1Context` connection string. `dotnet ef dbcontext list` now returns only `AppDbContext` |
| 35 | `NavMenu.razor`'s `.nav-section-label` class uses `color: rgba(255,255,255,0.25)` (near-transparent white) against the light sidebar background used by every role's nav — the section label ("Admin", "Teacher", "Learner", and now "Parent") is present in the DOM but effectively invisible. Same root cause as the earlier washed-out-modal-heading issue (background/foreground not contrasting), not fixed by that pass since it didn't touch this class; confirmed still present for the new Parent section | Open |
