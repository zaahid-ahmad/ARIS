# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run the application
dotnet run

# Build
dotnet build

# Add a new EF Core migration
dotnet ef migrations add <MigrationName>

# Apply pending migrations to the database
dotnet ef database update

# Revert to a specific migration
dotnet ef database update <MigrationName>
```

The app runs at `http://aris1.dev.localhost:5149` (or `https://aris1.dev.localhost:7124`).

## Tech Stack

- **ASP.NET Core 10 Blazor Server** with Interactive Server render mode
- **EF Core 10** + SQL Server LocalDB (`ARIS_Prototype` database)
- **ASP.NET Core Identity** with a custom `User` model

## Architecture

### Multi-Tenancy Model

Everything is scoped to a `School`. The five roles are:

| Role | SchoolId | Access |
|------|----------|--------|
| `SuperAdmin` | `null` | All schools |
| `Admin` | required | Their school only |
| `Teacher` | required | Their school only |
| `Learner` | required | Their school only |
| `Parent` | required | Their own linked children only |

`SchoolAuthorizationService` enforces this: it checks the current user's `SchoolId` and gates access to subjects and other school-scoped data. SuperAdmin bypasses all school checks. For `Parent`, access isn't school-scoped but link-scoped: `GetAccessibleLearnerIds()` and `HasAccessToLearner()` resolve the calling user's `Parent` → `ParentLearner` rows to determine which specific learners they may view — both deny-by-default (never throw, return empty/`false` on any failure).

### Page Structure (Blazor Components)

Pages live under `Components/Pages/` organized by role:
- `SuperAdmin/` — school management, admin management
- `Admin/` — user creation, subject management, learner enrollment
- `Teacher/` — marks entry, attendance, at-risk view, learner profiles
- `Learner/` — dashboard, marks, attendance, support (interventions)
- `Parent/` — dashboard (children list), marks, attendance, overview (alerts) — each per-child page takes a `{LearnerId}` route parameter

Each page uses `@attribute [Authorize(Roles = "...")]` to restrict access. The three `Parent/` per-child pages additionally re-check `HasAccessToLearner` inside `OnParametersSetAsync` (not `OnInitializedAsync`) so that a route-parameter change on an already-live component instance re-validates ownership rather than trusting whatever check ran when the component was first constructed.

### Classes

`SchoolClass` (`SchoolId` + `Grade` + `Name`, unique together) is the admin-managed list of classes a learner can belong to, set up under `/admin/classes` (nested by Grade 10/11/12). `Learner.ClassId` is a required FK to it — `CreateUser.razor` and `UserManagement.razor`'s Edit Class modal both present it as a Grade-then-Class dropdown pair rather than free text, and `BulkUserImportService` resolves a CSV's `ClassName` text column against existing classes (accepting both the short form, e.g. `"A"`, and the legacy long form, e.g. `"10A"`) rather than creating one on the fly — the class must already exist. Deleting a class with learners still assigned is blocked (`DeleteBehavior.Restrict`).

### Data Flow for Assessments & Marks

1. Admin creates `Subject` → Teacher creates `AssessmentType` (e.g. "SBA", "Exam") → Teacher creates `Assessment` (linked to type + term)
2. Teacher enters `LearnerMark` per learner per assessment. Assessments can have `AssessmentQuestion` sub-items, tracked via `LearnerQuestionMark`.
3. `WeightCalculationService.CalculateWeightedTermMark()` computes a learner's weighted term percentage using the `WeightingStructure` for that subject/term. Weights must sum to 100% at each level (validated by `WeightingService.ValidateWeightingStructure()`). `CalculateYearMark()` rolls several terms' marks (plus, optionally, a standalone exam `AssessmentType`) into a year/promotion mark — see Weighting System below.
4. APS levels (0–7, South African system) are resolved from custom `GradeBand` rows per subject, falling back to default percentage bands.

### Intervention System

`InterventionService.GenerateInterventions()` is called after marking a `LearnerQuestionMark`. It calculates a percentage and creates or updates an `Intervention` record with one of five levels: `Critical` (≤30%), `Attention` (≤55%), `Focus` (≤65%), `Minor` (≤79%), `WellDone` (>79%). Interventions are shown to learners under `/learner/support`.

### Risk Assessment

`RiskAssessmentService.CalculateRiskScore(learnerId, subjectId)` computes a composite score (60% academic average + 30% attendance + 10% trend) and maps it to a level: `Critical` (score ≥55), `High` (≥`AtRiskThreshold` = 45), `Moderate` (≥30), `Low` otherwise. Two entry points share one scoring implementation (`BuildRiskData`, private): the single-learner `CalculateRiskScore`, and `CalculateRiskScoresForSubject(subjectId, learnerIds)` — a batched version that does a handful of DB queries per distinct current-term among the group instead of several per learner. Any page walking every learner in a subject (`Teacher/Dashboard.razor`'s per-subject risk-donut cards, `Teacher/AtRisk.razor`'s scatter chart/table) must use the batched call — the per-learner version turns into hundreds of sequential round trips once a subject has dozens of enrolled learners.

The academic 60% is sourced from `WeightCalculationService`, not a raw average of every mark: each learner's own **current term** — the highest term for which *that learner* has a recorded mark, resolved per learner, not subject-wide — feeds `CalculateWeightedTermMark`/`CalculateWeightedTermMarksForSubject`. If a `WeightingStructure` is properly configured for that term, the type-weighted percentage is used; otherwise it falls back to a simple average of just that term's marks (never blended across the subject's whole history). Resolving "current term" per learner (not via a single subject-wide `MAX(Assessment.Term)`) matters: a subject-wide version would make every learner who hasn't been marked yet in a brand-new term look like they have a 0% academic average the moment *any* assessment exists for it, even before the rest of the class has been captured — this was caught live during verification (one early Term 4 mark for one learner briefly showed 83 of 84 classmates as Critical).

The trend 10% is `ResolveTrendFactor`, computed from the learner's own assessment history, not term averages: their non-absent `LearnerMark`s for the subject, ordered chronologically by `Assessment.Date` with no term filter, windowed to the last 6 (or fewer). That window is split in half by position — the average of the recent half is compared against the average of the earlier half, delta clamped to ±20 percentage points and mapped linearly onto `[0, 10]`, centered at `5` for no change or fewer than 2 marks (neutral, same as a learner with no history). Deliberately not term-to-term — a decline appearing late in one term doesn't have to wait for a full next-term average to register. Both `CalculateRiskScore` and `CalculateRiskScoresForSubject` batch this the same way as the academic average: one query for the whole subject's mark history (grouped in memory per learner), independent of the per-term grouping used for the academic average.

`Teacher/Dashboard.razor` shows one card per subject the teacher teaches, each with an inline-SVG donut (stroke-dasharray technique, no charting library) breaking that subject's enrolled learners down by risk band, plus a click-through banner for the total at-risk count across all subjects — both fed by the same batched risk walk (one pass over the data, not two).

### Weighting System

`WeightingStructure` (per subject+term, unique index) contains a tree of `WeightingNode` records. Nodes are self-referencing (parent/child) and can be of type `AssessmentType`, `Assessment`, `Custom`, `Term`, or `Task`. A flat structure (all root nodes) is the common case for a single term, set up via `WeightingService.CreateSimpleWeighting()`.

`WeightingStructure.Term == 0` is a reserved sentinel (not a real term) marking a subject's **Year structure** — how its term marks and assessment types roll up into a year/promotion mark, configured under `Admin/SubjectManagement.razor`'s "Year Weighting" tab. Shape is arbitrary (not hardcoded to any fixed SBA/exam split): a `Custom` node is a pure group for children, a `Term` node substitutes in that term's weighted mark (`WeightingNode.ReferencedTerm` says which), an `AssessmentType` node substitutes in that one type's own percentage standalone (e.g. a Final Exam). `WeightCalculationService.CalculateYearMark(learnerId, subjectId)` evaluates the tree recursively, built almost entirely out of `CalculateWeightedTermMark` — no separate percentage math. Surfaced as an "Overall: Term 1... Year Mark..." summary on `Teacher/LearnerProfiles.razor` and replacing the old flat/unweighted average on `Learner/Marks.razor` and `Parent/Marks.razor`.

**Grade 12 subjects only have Terms 1–3** (Admin/SubjectManagement.razor's term selector and Year Weighting term picker both restrict to `{1,2,3}` when `Subject.Grade == 12`, with a server-side guard on assessment/type creation too) — the real NSC final exam mark is held externally by the Department of Basic Education and isn't captured in ARIS, so Term 4 doesn't exist in this system for Grade 12.

### Database & Seeding

`AppDbContext` extends `IdentityDbContext<User>`. On startup, `DbSeeder.SeedAsync()` creates the five roles, a `Default School`, and default accounts:
- `superadmin@aris.com` / `SuperAdmin@1234`
- `admin@aris.com` / `Admin@1234`

Most FK relationships use `DeleteBehavior.Restrict` to avoid SQL Server cascade path conflicts. Exceptions: `AssessmentQuestion → Cascade`, `Intervention → Learner Cascade`, `WeightingNode → WeightingStructure Cascade`.
