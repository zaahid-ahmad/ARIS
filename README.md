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

### Demo data (optional)

To load a full demo school (Riverside Secondary School) with teachers, learners,
parents, subjects, marks and attendance, run this **once** from a terminal in the
project folder (the folder containing `ARIS1.csproj`):

```
dotnet run -- --seed-demo
```

Seeding takes a few minutes. After that, launch normally (F5). Demo logins all use
school code **RIVERSIDE**:

| Role    | Example email                                   | Password       |
|---------|-------------------------------------------------|----------------|
| Admin   | admin@riverside.school.co.za                    | Admin@1234     |
| Teacher | daniel.robinson@riverside.school.co.za          | Teacher@1234   |
| Learner | andile.venter@learner.riverside.school.co.za    | Learner@1234   |
| Parent  | michael.venter@outlook.com                      | Parent@1234    |

> The demo parents have realistic-looking email addresses. Keep the email
> "redirect" setting below turned on so they are never actually emailed.

---

## Optional: AI Chatbot and Email Setup

The app runs fully **without** this step. The AI chatbot and real email sending
need personal credentials, which are kept in .NET **user-secrets**. They are stored
in your Windows user profile, never in the project folder, the zip, or git, so
**each team member sets up their own**.

| Feature | Without setup | With setup |
|---|---|---|
| Learner Chat Assistant | Basic rule-based answers | AI answers (Groq), focused on the CAPS curriculum |
| Emails (password reset, welcome emails, parent messages, risk alerts, progress reports) | Recorded in **Admin → Email Log** as *Skipped* — nothing is sent | Sent through your Gmail account to your own inbox |

Run the commands below from a terminal in the project folder (the folder containing
`ARIS1.csproj`). In Visual Studio you can use **View > Terminal**.

### 1. AI chatbot (Groq)

Create your own free API key at https://console.groq.com/keys, then run:

```
dotnet user-secrets set "ChatAssistant:ApiKey" "<your Groq API key>"
```

Use your own key rather than sharing one: the free tier limit (8,000 tokens per
minute) applies per key, so a shared key throttles the whole team.

### 2. Email (Gmail)

1. On your Google account, turn on **2-Step Verification** (Google Account → Security).
2. Create an **App password** (Google Account → Security → App passwords, name it "ARIS")
   and copy the 16-character code.
3. Run:

```
dotnet user-secrets set "Email:Host" "smtp.gmail.com"
dotnet user-secrets set "Email:Port" "587"
dotnet user-secrets set "Email:Username" "<your gmail address>"
dotnet user-secrets set "Email:Password" "<16-character app password, no spaces>"
dotnet user-secrets set "Email:FromAddress" "<your gmail address>"
dotnet user-secrets set "Email:FromName" "ARIS"
dotnet user-secrets set "Email:RedirectAllTo" "<your own inbox>"
```

- **`Email:RedirectAllTo` is required.** Every email ARIS sends goes to this inbox
  instead of the real recipient (the original recipient is shown in the subject).
  Without it, emails are not sent at all.
- **Never set `Email:AllowRealRecipients`** while using demo data.
- Revoke the app password (Google Account → Security → App passwords) when you're done testing.

### 3. Check or undo

```
dotnet user-secrets list                      # shows what's set (includes values, so don't share screenshots of it)
dotnet user-secrets remove "Email:Password"   # remove one setting
dotnet user-secrets clear                     # remove all ARIS secrets on this machine
```

Restart the app after changing secrets.

---

## Technology Stack

- **Blazor Server** (.NET 10) — interactive server-side UI
- **Entity Framework Core** — data access and migrations
- **SQL Server LocalDB** — database (bundled with Visual Studio)
- **ASP.NET Core Identity** — authentication and role management
- **Bootstrap** — UI styling
- **MailKit** — SMTP email (optional, see setup above)
- **Groq API** (OpenAI-compatible) — AI chatbot (optional, see setup above)

---

## Troubleshooting

- **Build fails on the target framework** — the machine is missing the .NET 10 SDK.
  Update Visual Studio or install the SDK, then reopen the solution.
- **Chatbot gives basic, repetitive answers** — no Groq key is set on this machine
  (or the free-tier limit was reached for a moment). See *Optional: AI Chatbot and Email Setup*.
- **Emails show as *Skipped* in Admin → Email Log** — email isn't configured, or
  `Email:RedirectAllTo` isn't set. The *Error* column says which.
- **Emails show as *Failed* with `535 5.7.8`** — the Gmail app password is wrong or was
  revoked, or 2-Step Verification is off. Create a new app password, update
  `Email:Password`, restart, then click **Retry** in the Email Log.
- **Emails show as *Failed* with `534 5.7.14`** — Google blocked the sign-in (common on
  brand-new Gmail accounts). Sign in to that Gmail in a browser, confirm "Yes, it was me"
  under Google Account → Security, then retry.
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
