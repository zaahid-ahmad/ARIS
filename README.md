# ARIS — Academic Risk Intelligence System

**Module:** IS Project (PRO001)
**Sprint:** Sprint 5
**Group:** Ubuntu Tech

---

## Overview

ARIS is a school management and academic-risk platform built with Blazor Server
(.NET 10). It supports multiple schools (multi-tenancy) and four user roles —
SuperAdmin, Admin, Teacher, and Learner — covering user management, subject and
assessment setup, learner enrollment, mark capture, weighted score calculation,
and automatic flagging of learners who need intervention.

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
> target framework, update Visual Studio (or install the .NET 10 SDK from
> https://dotnet.microsoft.com/download) and reopen.

---

## How to Run

1. Extract the submitted zip to a folder.
2. Open **`ARIS1.slnx`** in Visual Studio.
3. Wait for NuGet to restore packages (happens automatically on first open).
4. Press **F5** (or click the green Run button).

On first launch the application **creates and seeds its own database
automatically** — there is no manual database setup, no scripts to run, and no
connection string to change. The app builds the database via Entity Framework
Core migrations and seeds the default roles, a default school, and the login
accounts below.

The app opens in your browser at the address shown in the launch window
(typically `https://localhost:xxxx`).

---

## Login Accounts

The following accounts are created automatically on first run:

| Role       | Email                   | Password          | School code	|
|------------|-------------------------|-------------------|----------------|
| SuperAdmin | superadmin@aris.com     | SuperAdmin@1234   | SUPERADMIN		|
| Admin      | admin@aris.com          | Admin@1234        | DEFAULT		|

- **SuperAdmin** manages schools and creates school administrators.
- **Admin** manages users, subjects, assessments, and enrollment within their
  school (the seeded "Default School").

Teacher and Learner accounts are created from within the app by an Admin.

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
