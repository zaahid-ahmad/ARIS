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
3. `WeightCalculationService.CalculateWeightedTermMark()` computes a learner's weighted term percentage using the `WeightingStructure` for that subject/term. Weights must sum to 100% at each level (validated by `WeightingService.ValidateWeightingStructure()`).
4. APS levels (0–7, South African system) are resolved from custom `GradeBand` rows per subject, falling back to default percentage bands.

### Intervention System

`InterventionService.GenerateInterventions()` is called after marking a `LearnerQuestionMark`. It calculates a percentage and creates or updates an `Intervention` record with one of five levels: `Critical` (≤30%), `Attention` (≤55%), `Focus` (≤65%), `Minor` (≤79%), `WellDone` (>79%). Interventions are shown to learners under `/learner/support`.

### Weighting System

`WeightingStructure` (per subject+term, unique index) contains a tree of `WeightingNode` records. Nodes are self-referencing (parent/child) and can be of type `AssessmentType`, `Assessment`, `Custom`, or `Task`. A flat structure (all root nodes) is the common case, set up via `WeightingService.CreateSimpleWeighting()`.

### Database & Seeding

`AppDbContext` extends `IdentityDbContext<User>`. On startup, `DbSeeder.SeedAsync()` creates the five roles, a `Default School`, and default accounts:
- `superadmin@aris.com` / `SuperAdmin@1234`
- `admin@aris.com` / `Admin@1234`

Most FK relationships use `DeleteBehavior.Restrict` to avoid SQL Server cascade path conflicts. Exceptions: `AssessmentQuestion → Cascade`, `Intervention → Learner Cascade`, `WeightingNode → WeightingStructure Cascade`.
