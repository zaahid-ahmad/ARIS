# ARIS — Academic Risk Intelligence System

**Module:** IS Project (PRO001)
**Sprint:** Sprint 5
**Group:** Ubuntu Tech

---

## Overview

ARIS is a school management and academic-risk platform built with Blazor Server
(.NET 10). It supports multiple schools (multi-tenancy) and five user roles —
SuperAdmin, Admin, Teacher, Learner, and Parent — covering user management,
subject and assessment setup, class management, learner enrollment, mark
capture, weighted score calculation, automatic flagging of learners who need
intervention, and a read-only portal for parents to view their own children's
progress.

---

## Prerequisites

To build and run this project, the machine needs:

- **.NET 10 SDK** — the project targets .NET 10 and will not build on earlier versions.
- **Visual Studio 2026 (Visual Studio 18.0 or newer)**, with the *ASP.NET and web
  development* workload. A current Visual Studio install includes everything else
  needed, including **SQL Server LocalDB**, which the app uses for its database.
- An **internet connection on first build**, so NuGet can restore the project's
  packages.

> **Note on the .NET version:** .NET 10 is recent, so an older Visual Studio install
> may not include it. If the solution fails to load or build with a message about the
> target framework, update Visual Studio (or if using Visual Studio 2026, install the .NET 10 SDK from
> https://dotnet.microsoft.com/download) and reopen.

---

## How to Run

1. Extract the submitted zip to a folder.
2. Open **`ARIS1.slnx`** in Visual Studio.
3. Wait for NuGet to restore packages (happens automatically on first open).
4. Build the solution (press **Ctrl+Shift+B** or select **Build > Build Solution** from the menu).
5. Press **F5** (or click the green Run button).

On first launch the application **creates and seeds its own database
automatically** — there is no manual database setup, no scripts to run, and no
connection string to change. The app builds the database via Entity Framework
Core migrations and seeds the default roles, a default school, and the login
accounts below.

The app opens in your browser automatically, at
`https://aris1.dev.localhost:7124` (see the note on this custom hostname under
Troubleshooting below if it doesn't load).

---

## Login Accounts

The following accounts are created automatically on first run:

| Role       | Email                   | Password          | School code	|
|------------|-------------------------|-------------------|----------------|
| SuperAdmin | superadmin@aris.com     | SuperAdmin@1234   | SUPERADMIN		|
| Admin      | admin@aris.com          | Admin@1234        | DEFAULT		|

- **SuperAdmin** manages schools and creates school administrators.
- **Admin** manages users, subjects, classes, assessments, and enrollment
  within their school (the seeded "Default School").

Teacher, Learner, and Parent accounts are created from within the app by an
Admin — either one at a time, or in bulk via CSV/Excel import.

---

## Technology Stack

- **Blazor Server** (.NET 10) — interactive server-side UI
- **Entity Framework Core** — data access and migrations
- **SQL Server LocalDB** — database (bundled with Visual Studio)
- **ASP.NET Core Identity** — authentication and role management
- **Bootstrap** — UI styling

---

## Troubleshooting

- **Build fails on the target framework** — the machine is missing the .NET 10 SDK.
  Update Visual Studio or install the SDK, then reopen the solution.
- **A database or login error on first run** — confirm SQL Server LocalDB is
  installed (it ships with Visual Studio's *ASP.NET and web development* workload).
- **The database does not need to be created manually.** If you previously ran the
  app and want a clean database, it can be dropped and it will be recreated and
  reseeded on the next launch.
- **The app fails to start, or the browser can't reach the page, because of the
  hostname** — the launch profiles use `aris1.dev.localhost` instead of plain
  `localhost` (so multiple projects on this machine can each have their own
  cookie-scoped address). Modern Windows and Chromium-based browsers (Edge,
  Chrome) resolve any `*.localhost` address to your own machine automatically,
  with nothing to configure — this is how the app runs out of the box on most
  machines. If yours doesn't (an older Windows build, or a locked-down network
  config), you'll see either a Kestrel error on startup saying the host can't be
  resolved, or the browser reporting it can't find the page. The fix is a single
  line added to the hosts file at
  `C:\Windows\System32\drivers\etc\hosts` (as Administrator):
  ```
  127.0.0.1 aris1.dev.localhost
  ```
  Save the file and press F5 again — no project changes are needed.
