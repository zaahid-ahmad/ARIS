# ARIS UI Rebuild Guide — Clean Rebuild on Original Prototype

This rebuilds everything we did across the whole UI-sync project, starting fresh from
`ARIS-main-sprint-6.zip` (the untouched original). It deliberately **skips** the global
render-mode change that caused the "Not Found" errors — that step is not part of this
guide anywhere. Sidebar collapse is done with a few lines of plain JavaScript instead of
Blazor server state, which sidesteps the whole problem entirely and is honestly simpler.

Work through this in order. Each step only depends on the ones before it.

---

## STEP 0 — What NOT to do this time

Do **not**, anywhere in this rebuild:
- Add `@rendermode="InteractiveServer"` to `<Routes />` in `App.razor`
- Add `@rendermode InteractiveServer` directly to `MainLayout.razor`

Leave `Components/App.razor`'s `<Routes />` and `Components/Layout/MainLayout.razor`'s
`@inherits LayoutComponentBase` line exactly as they already are in the fresh zip. The
19 individual pages that already have `@rendermode InteractiveServer` on them (Dashboard,
UserManagement, etc.) — leave those alone too, they were already there in the original
and the app needs them for actual functionality (forms, buttons, live calculations).

---

## STEP 1 — Design system: fonts + stylesheet

**Copy this file in:**
`wwwroot/aris-styles.css` — you already have this file from before (it's the ~1,200-line
design system file). Copy it into this fresh project's `wwwroot/` folder the same way as
last time (File Explorer copy/paste, or Visual Studio "Add Existing Item").

**Go to `Components/App.razor`. Find:**
```html
    <ResourcePreloader />
    <link rel="stylesheet" href="@Assets["lib/bootstrap/dist/css/bootstrap.min.css"]" />
    <link rel="stylesheet" href="@Assets["app.css"]" />
    <link rel="stylesheet" href="@Assets["ARIS1.styles.css"]" />
```
**Replace with:**
```html
    <ResourcePreloader />
    <link href="https://fonts.googleapis.com/css2?family=DM+Sans:ital,wght@0,300;0,400;0,500;0,600;0,700;1,400&family=DM+Mono:wght@400;500&display=swap" rel="stylesheet">
    <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css">
    <link rel="stylesheet" href="@Assets["lib/bootstrap/dist/css/bootstrap.min.css"]" />
    <link rel="stylesheet" href="@Assets["app.css"]" />
    <link rel="stylesheet" href="aris-styles.css" />
    <link rel="stylesheet" href="@Assets["ARIS1.styles.css"]" />
```

---

## STEP 2 — Claims factory (name/avatar support)

**Create new file** `Services/AppUserClaimsPrincipalFactory.cs`:
```csharp
using System.Security.Claims;
using ARIS1.Models;
using Microsoft.AspNetCore.Identity;

namespace ARIS1.Services
{
    public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<User, IdentityRole>
    {
        public AppUserClaimsPrincipalFactory(
            UserManager<User> userManager,
            RoleManager<IdentityRole> roleManager,
            Microsoft.Extensions.Options.IOptions<IdentityOptions> optionsAccessor)
            : base(userManager, roleManager, optionsAccessor)
        {
        }

        public override async Task<ClaimsPrincipal> CreateAsync(User user)
        {
            var principal = await base.CreateAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;

            if (!string.IsNullOrWhiteSpace(user.Fullname))
                identity.AddClaim(new Claim("Fullname", user.Fullname));

            if (user.SchoolId.HasValue)
                identity.AddClaim(new Claim("SchoolId", user.SchoolId.Value.ToString()));

            return principal;
        }
    }
}
```

**Go to `Program.cs`. Find:**
```csharp
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();
```
**Replace with:**
```csharp
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager()
.AddClaimsPrincipalFactory<ARIS1.Services.AppUserClaimsPrincipalFactory>()
.AddDefaultTokenProviders();
```

---

## STEP 3 — Login page

**Create new file** `Components/Account/Pages/Login.razor.css` — copy this from your
previous project's copy of the same file (the gradient-background/card-style CSS).

**Go to `Components/Account/Pages/Login.razor`. Find:**
```html
<PageTitle>Log in</PageTitle>

<div class="row justify-content-center mt-5">
    <div class="col-md-4">
        <h2 class="text-center mb-4">ARIS Login</h2>
        <StatusMessage Message="@errorMessage" />
        <EditForm EditContext="editContext" method="post" OnSubmit="LoginUser" FormName="login">
            <DataAnnotationsValidator />
            <div class="form-floating mb-3">
                <InputText @bind-Value="Input.Email" id="email" class="form-control" placeholder="name@example.com" />
                <label for="email">Email</label>
                <ValidationMessage For="() => Input.Email" class="text-danger" />
            </div>
            <div class="form-floating mb-3">
                <InputText type="password" @bind-Value="Input.Password" id="password" class="form-control" placeholder="password" />
                <label for="password">Password</label>
                <ValidationMessage For="() => Input.Password" class="text-danger" />
            </div>
            <div class="form-floating mb-3">
                <InputText @bind-Value="Input.SchoolCode" id="schoolCode" class="form-control" placeholder="school code" />
                <label for="schoolCode">School Code</label>
                <ValidationMessage For="() => Input.SchoolCode" class="text-danger" />
            </div>
            <button type="submit" class="w-100 btn btn-lg btn-primary">Log in</button>
        </EditForm>
    </div>
</div>
```
**Replace with:**
```html
<PageTitle>Log in | ARIS</PageTitle>

<div class="aris-login login-wrap">
    <div class="login-card">
        <div class="login-brand">
            <div class="login-logo"><i class="fas fa-graduation-cap"></i></div>
            <div class="login-brand-text">
                <h2>ARIS</h2>
                <p>Academic Risk Intelligence System</p>
            </div>
        </div>

        <EditForm EditContext="editContext" method="post" OnSubmit="LoginUser" FormName="login">
            <DataAnnotationsValidator />

            <div class="login-field">
                <label for="email">Email Address</label>
                <InputText @bind-Value="Input.Email" id="email" placeholder="your@email.com" autocomplete="email" />
                <ValidationMessage For="() => Input.Email" class="field-error" />
            </div>

            <div class="login-field">
                <label for="schoolCode">School Code</label>
                <InputText @bind-Value="Input.SchoolCode" id="schoolCode" placeholder="e.g. 321" />
                <ValidationMessage For="() => Input.SchoolCode" class="field-error" />
            </div>

            <div class="login-field">
                <label for="password">Password</label>
                <InputText type="password" @bind-Value="Input.Password" id="password" placeholder="••••••" autocomplete="current-password" />
                <ValidationMessage For="() => Input.Password" class="field-error" />
            </div>

            @if (!string.IsNullOrEmpty(errorMessage))
            {
                <div class="login-error show">@errorMessage</div>
            }

            <button type="submit" class="login-btn"><i class="fas fa-sign-in-alt"></i>&nbsp; Sign In</button>
        </EditForm>
    </div>
</div>
```

**Do not touch the `@code` block yet** — one small fix to it comes in Step 7. Leave
`LoginUser()` as-is for now.

---

## STEP 4 — MainLayout markup + stylesheet (no render mode!)

**Create new file** `Components/Layout/MainLayout.razor.css` with this content:
```css
.app-shell {
    --ink: #0f1923;
    --ink-2: #2d3d4f;
    --ink-3: #5a6e82;
    --ink-4: #8fa3b5;
    --surface: #f4f6f9;
    --surface-2: #edf0f4;
    --white: #ffffff;
    --border: #dde3ea;
    --border-2: #c8d1db;
    --blue: #1a56c4;
    --blue-dark: #1240a0;
    --blue-light: #e8eef9;
    --teal: #0891b2;
    --red: #dc2626;
    --sidebar-w: 260px;
    --radius: 10px;
    --radius-lg: 14px;
    --shadow-sm: 0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04);
    --shadow: 0 4px 12px rgba(0,0,0,0.06), 0 1px 3px rgba(0,0,0,0.04);
    display: flex;
    min-height: 100vh;
    background: var(--surface);
}

.sidebar {
    width: var(--sidebar-w);
    background: var(--white);
    display: flex;
    flex-direction: column;
    position: fixed;
    top: 0;
    left: 0;
    bottom: 0;
    z-index: 200;
    overflow: hidden;
    border-radius: 0 16px 16px 0;
    box-shadow: var(--shadow);
    transition: width 0.3s ease;
}

.sidebar.collapsed {
    width: 60px;
}

.sidebar.collapsed ~ .main-area {
    margin-left: 60px;
}

.sidebar-brand {
    padding: 1.25rem 1.25rem 1rem;
    display: flex;
    align-items: center;
    gap: 10px;
}

.sidebar-logo {
    width: 36px;
    height: 36px;
    border-radius: 10px;
    background: linear-gradient(135deg, var(--blue), var(--teal));
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
}

    .sidebar-logo i {
        color: white;
        font-size: 0.9rem;
    }

.sidebar-brand-text {
    font-size: 1.25rem;
    font-weight: 700;
    letter-spacing: -0.02em;
    background: linear-gradient(135deg, #2c0e5c 0%, #1a56c4 70%, #4a00e0 100%);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
    background-clip: text;
}

.sidebar-bottom {
    padding: 0.75rem;
    border-top: 1px solid var(--border);
    margin-top: auto;
}

.sidebar-action {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 9px 10px;
    border-radius: 8px;
    cursor: pointer;
    color: var(--ink-3);
    font-size: 0.85rem;
    font-weight: 500;
    transition: all 0.15s;
    border: none;
    background: transparent;
    width: 100%;
    text-align: left;
}

    .sidebar-action:hover {
        background: var(--surface);
        color: var(--red);
    }

    .sidebar-action i {
        width: 16px;
        text-align: center;
        font-size: 0.85rem;
    }

.main-area {
    margin-left: var(--sidebar-w);
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    transition: margin-left 0.3s ease;
}

.top-icons-bar {
    padding: 0.9rem 1.5rem;
    display: flex;
    align-items: center;
    justify-content: space-between;
    position: sticky;
    top: 0;
    z-index: 100;
    background: var(--surface);
}

.page-title-area {
    flex: 1;
}

.header-title {
    font-size: 0.95rem;
    font-weight: 700;
    color: var(--ink);
    letter-spacing: -0.02em;
}

.header-sub {
    font-size: 0.72rem;
    color: var(--ink-3);
    margin-top: 1px;
}

.header-actions {
    display: flex;
    align-items: center;
    gap: 0.75rem;
}

.page-content {
    padding: 1.5rem;
    flex: 1;
}

@media (max-width: 768px) {
    .sidebar {
        width: 64px;
        border-radius: 0;
    }

    .sidebar-brand-text {
        display: none;
    }

    .main-area {
        margin-left: 64px;
    }
}

#blazor-error-ui {
    color-scheme: light only;
    background: lightyellow;
    bottom: 0;
    box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
    box-sizing: border-box;
    display: none;
    left: 0;
    padding: 0.6rem 1.25rem 0.7rem 1.25rem;
    position: fixed;
    width: 100%;
    z-index: 1000;
}

    #blazor-error-ui .dismiss {
        cursor: pointer;
        position: absolute;
        right: 0.75rem;
        top: 0.5rem;
    }
```

**Go to `Components/Layout/MainLayout.razor`. Replace the ENTIRE file** with:
```razor
@inherits LayoutComponentBase

<AuthorizeView>
    <Authorized>
        <div class="app-shell active">
            <nav class="sidebar" id="arisSidebar">
                <div class="sidebar-brand">
                    <div class="sidebar-logo" onclick="toggleAppSidebar()" style="cursor:pointer;"><i class="fas fa-graduation-cap"></i></div>
                    <div class="sidebar-brand-text">ARIS</div>
                </div>

                <NavMenu />

                <AuthorizeView Roles="Learner" Context="chatContext">
                    <Authorized>
                        <div class="sidebar-ai-section">
                            <a href="/learner/support" class="ai-action">
                                <span class="ai-icon"><i class="fas fa-robot"></i></span>
                                <span>Chat Assistant</span>
                            </a>
                        </div>
                    </Authorized>
                </AuthorizeView>

                <div class="sidebar-bottom">
                    <form action="/Account/Logout" method="post">
                        <AntiforgeryToken />
                        <input type="hidden" name="ReturnUrl" value="@currentUrl" />
                        <button type="submit" class="sidebar-action">
                            <i class="fas fa-sign-out-alt"></i> <span>Sign Out</span>
                        </button>
                    </form>
                </div>
            </nav>

            <div class="main-area">
                <div class="top-icons-bar">
                    <div class="page-title-area">
                        <div class="header-title">@GetPageTitle()</div>
                        <div class="header-sub">@RoleLabel(context.User)</div>
                    </div>
                    <div class="header-actions" style="position:relative;">
                        <button class="hdr-btn profile-btn" onclick="toggleAppProfilePanel()" title="My Profile">
                            <span class="profile-avatar" style="background:@AvatarColor(context.User)">@GetInitials(DisplayNameOf(context.User))</span>
                        </button>
                        <div class="profile-panel" id="arisProfilePanel">
                            <div class="profile-panel-header">
                                <div class="profile-panel-avatar" style="background:@AvatarColor(context.User)"><i class="fas @RoleIcon(context.User)"></i></div>
                                <div>
                                    <div class="profile-panel-name">@DisplayNameOf(context.User)</div>
                                </div>
                            </div>
                            <div class="profile-panel-body">
                                <a class="profile-panel-btn" href="@ProfileHref(context.User)">
                                    <i class="fas fa-user-edit"></i> Edit Profile
                                </a>
                                <a class="profile-panel-btn" href="/Account/Manage/ChangePassword">
                                    <i class="fas fa-key"></i> Change Password
                                </a>
                            </div>
                            <div class="profile-panel-footer">
                                <form action="/Account/Logout" method="post">
                                    <AntiforgeryToken />
                                    <input type="hidden" name="ReturnUrl" value="@currentUrl" />
                                    <button type="submit" class="profile-panel-btn signout-btn">
                                        <i class="fas fa-sign-out-alt"></i> Sign Out
                                    </button>
                                </form>
                            </div>
                        </div>
                    </div>
                </div>

                <div class="page-content">
                    @Body
                </div>
            </div>
        </div>
    </Authorized>
    <NotAuthorized>
        @Body
    </NotAuthorized>
</AuthorizeView>

<div id="blazor-error-ui" data-nosnippet>
    An unhandled error has occurred.
    <a href="." class="reload">Reload</a>
    <span class="dismiss">🗙</span>
</div>

<script>
    function toggleAppSidebar() {
        const sidebar = document.getElementById('arisSidebar');
        if (!sidebar) return;
        sidebar.classList.toggle('collapsed');
        localStorage.setItem('aris-sidebar-collapsed', sidebar.classList.contains('collapsed'));
    }

    function toggleAppProfilePanel() {
        const panel = document.getElementById('arisProfilePanel');
        if (!panel) return;
        panel.classList.toggle('open');
    }

    function restoreAppSidebarState() {
        const collapsed = localStorage.getItem('aris-sidebar-collapsed') === 'true';
        const sidebar = document.getElementById('arisSidebar');
        if (sidebar) sidebar.classList.toggle('collapsed', collapsed);
    }

    document.addEventListener('DOMContentLoaded', restoreAppSidebarState);
    document.addEventListener('enhancedload', restoreAppSidebarState);

    document.addEventListener('click', function (e) {
        const panel = document.getElementById('arisProfilePanel');
        const btn = e.target.closest('.profile-btn');
        if (panel && panel.classList.contains('open') && !panel.contains(e.target) && !btn) {
            panel.classList.remove('open');
        }
    });
</script>

@code {
    private string? currentUrl;

    protected override void OnInitialized()
    {
        currentUrl = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
    }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private string DisplayNameOf(System.Security.Claims.ClaimsPrincipal user)
    {
        var fullname = user.FindFirst("Fullname")?.Value;
        return !string.IsNullOrWhiteSpace(fullname) ? fullname : (user.Identity?.Name ?? "?");
    }

    private string RoleOf(System.Security.Claims.ClaimsPrincipal user) => user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";

    private string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var local = name.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return local.Length > 0 ? local.Substring(0, Math.Min(2, local.Length)).ToUpper() : "?";
    }

    private string AvatarColor(System.Security.Claims.ClaimsPrincipal user) => RoleOf(user) switch
    {
        "SuperAdmin" => "var(--purple, #7c3aed)",
        "Admin" => "var(--blue, #1a56c4)",
        "Teacher" => "var(--orange, #d97706)",
        "Learner" => "var(--green, #16a34a)",
        _ => "var(--ink-3, #6b7280)"
    };

    private string RoleIcon(System.Security.Claims.ClaimsPrincipal user) => RoleOf(user) switch
    {
        "SuperAdmin" => "fa-user-shield",
        "Admin" => "fa-user-tie",
        "Teacher" => "fa-chalkboard-teacher",
        "Learner" => "fa-user-graduate",
        _ => "fa-user"
    };

    private string RoleLabel(System.Security.Claims.ClaimsPrincipal user) => RoleOf(user) switch
    {
        "SuperAdmin" => "Super Administrator",
        "Admin" => "School Administrator",
        "Teacher" => "Educator",
        "Learner" => "Learner",
        _ => "User"
    };

    private string ProfileHref(System.Security.Claims.ClaimsPrincipal user) => RoleOf(user) switch
    {
        "Learner" => "/learner/profile",
        _ => "/Account/Manage"
    };

    private string GetPageTitle()
    {
        var path = (currentUrl ?? "").Trim('/').ToLowerInvariant();
        return path switch
        {
            "" => "Home",
            "superadmin/dashboard" => "Dashboard",
            "superadmin/schools" => "Schools",
            "superadmin/admins" => "Staff Registry",
            "admin/dashboard" => "Dashboard",
            "admin/users" => "User Management",
            "admin/users/create" => "Create User",
            "admin/subjects" => "Subject Structure",
            "admin/enrollment" => "Learner Enrollment",
            "admin/attendance" => "Attendance",
            "teacher/dashboard" => "Dashboard",
            "teacher/marks" => "Mark Entry",
            "teacher/atrisk" => "Risk & Interventions",
            "teacher/attendance" => "Attendance",
            "teacher/learnerprofiles" => "Learner Profiles",
            "learner/dashboard" => "Dashboard",
            "learner/marks" => "Marks",
            "learner/attendance" => "Attendance",
            "learner/profile" => "My Profile",
            "learner/support" => "Chat Assistant",
            "account/manage" => "My Account",
            _ => "ARIS"
        };
    }
}
```

### Why this version is different from before, and why it's safer

- **No `@rendermode` anywhere in this file.** `MainLayout` still receives `Body`, so it's
  never allowed to declare its own render mode — that's the platform rule that broke
  things last time.
- **Collapse and the profile panel are done with plain `onclick="..."` calling small
  vanilla JavaScript functions** at the bottom of the file, not Blazor `@onclick`/C# state.
  Plain HTML `onclick` attributes work with zero server connection required — they run
  in the browser regardless of any Blazor render mode, so this can never trigger the
  `RenderFragment` error or interfere with routing.
- **The collapsed state is remembered** via `localStorage`, and restored on both a normal
  page load and Blazor's "enhanced navigation" (the `enhancedload` event) — so it stays
  collapsed as you move between pages, without needing a persistent server circuit.
- **`GetPageTitle()` and the role label are computed fresh on the server every time this
  component renders** — and because nothing here forces interactivity, every page
  navigation is a full server round-trip that re-runs this code from scratch. That's
  actually a feature here: the title, role label, and (in Step 6) the active nav
  highlight will always be correct for whatever page you're currently on, with no
  stale-state risk at all.

---

## STEP 5 — Remove the duplicate page name from each page body

Same list as before — delete just the one `<h2>` line from each file, nothing else:

| File | Delete this line |
|---|---|
| `Components/Pages/Admin/UserManagement.razor` | `<h2>User Management</h2>` |
| `Components/Pages/Admin/SubjectManagement.razor` | `<h2>Subject Management</h2>` |
| `Components/Pages/Admin/AttendanceManagement.razor` | `<h2>Attendance Management</h2>` |
| `Components/Pages/Admin/LearnerEnrollment.razor` | `<h2>Learner Enrollment</h2>` |
| `Components/Pages/Admin/Dashboard.razor` | `<h2>Admin Dashboard</h2>` |
| `Components/Pages/Admin/CreateUser.razor` | `<h2>Create New User</h2>` |
| `Components/Pages/Learner/Attendance.razor` | `<h2>My Attendance</h2>` |
| `Components/Pages/Learner/Marks.razor` | `<h2>My Marks</h2>` |
| `Components/Pages/Learner/Support.razor` | `<h2>Learning Support Hub</h2>` |
| `Components/Pages/Learner/Profile.razor` | `<h2>My Profile</h2>` |
| `Components/Pages/SuperAdmin/Admins.razor` | `<h2>Admins Management</h2>` |
| `Components/Pages/SuperAdmin/Schools.razor` | `<h2>Schools Management</h2>` |
| `Components/Pages/SuperAdmin/Dashboard.razor` | `<h2>SuperAdmin Dashboard</h2>` |
| `Components/Pages/Teacher/Attendance.razor` | `<h2>Capture Attendance</h2>` |
| `Components/Pages/Teacher/Marks.razor` | `<h2>Capture Marks</h2>` |
| `Components/Pages/Teacher/AtRisk.razor` | `<h2>At-Risk Learners</h2>` |
| `Components/Pages/Teacher/Dashboard.razor` | `<h2>Teacher Dashboard</h2>` |
| `Components/Pages/Teacher/LearnerProfiles.razor` | `<h2>Learner Profiles</h2>` |

Leave `Components/Pages/Learner/Dashboard.razor` alone — its `<h2>Welcome, @currentUser?.Fullname</h2>` is a personalized greeting, not a duplicate title.

Leave `NotFound.razor` and `Error.razor` alone too.

**"My Profile" is intentionally left out of the sidebar entirely** in the NavMenu below —
it's reachable through the header avatar → Edit Profile instead.

---

## STEP 6 — NavMenu (matches design, correct routes, active-state highlighting)

**Delete** `Components/Layout/NavMenu.razor.css` if it exists in this fresh copy (check —
it might not, since this is the original zip).

**Go to `Components/Layout/NavMenu.razor`. Replace the entire file** with:
```razor
@inject NavigationManager NavigationManager
@implements IDisposable

<nav class="sidebar-nav">
    <AuthorizeView Roles="SuperAdmin" Context="superAdminContext">
        <Authorized>
            <div class="nav-section-label">SuperAdmin</div>
            <a class="@NavClass("/superadmin/dashboard")" href="/superadmin/dashboard">
                <i class="fas fa-chart-line"></i> <span>Dashboard</span>
            </a>
            <a class="@NavClass("/superadmin/schools")" href="/superadmin/schools">
                <i class="fas fa-building"></i> <span>Schools</span>
            </a>
            <a class="@NavClass("/superadmin/admins")" href="/superadmin/admins">
                <i class="fas fa-users"></i> <span>Staff Registry</span>
            </a>
        </Authorized>
    </AuthorizeView>

    <AuthorizeView Roles="Admin" Context="adminContext">
        <Authorized>
            <div class="nav-section-label">Admin</div>
            <a class="@NavClass("/admin/dashboard")" href="/admin/dashboard">
                <i class="fas fa-chart-line"></i> <span>Dashboard</span>
            </a>
            <a class="@NavClass("/admin/users")" href="/admin/users">
                <i class="fas fa-users"></i> <span>User Management</span>
            </a>
            <a class="@NavClass("/admin/users/create")" href="/admin/users/create">
                <i class="fas fa-user-plus"></i> <span>Create User</span>
            </a>
            <a class="@NavClass("/admin/subjects")" href="/admin/subjects">
                <i class="fas fa-sitemap"></i> <span>Subject Structure</span>
            </a>
            <a class="@NavClass("/admin/enrollment")" href="/admin/enrollment">
                <i class="fas fa-user-graduate"></i> <span>Learner Enrollment</span>
            </a>
            <a class="@NavClass("/admin/attendance")" href="/admin/attendance">
                <i class="fas fa-calendar-check"></i> <span>Attendance</span>
            </a>
        </Authorized>
    </AuthorizeView>

    <AuthorizeView Roles="Teacher" Context="teacherContext">
        <Authorized>
            <div class="nav-section-label">Teacher</div>
            <a class="@NavClass("/teacher/dashboard")" href="/teacher/dashboard">
                <i class="fas fa-chart-line"></i> <span>Dashboard</span>
            </a>
            <a class="@NavClass("/teacher/marks")" href="/teacher/marks">
                <i class="fas fa-pen-ruler"></i> <span>Mark Entry</span>
            </a>
            <a class="@NavClass("/teacher/atrisk")" href="/teacher/atrisk">
                <i class="fas fa-exclamation-triangle"></i> <span>Risk &amp; Interventions</span>
            </a>
            <a class="@NavClass("/teacher/attendance")" href="/teacher/attendance">
                <i class="fas fa-calendar-check"></i> <span>Attendance</span>
            </a>
            <a class="@NavClass("/teacher/learnerprofiles")" href="/teacher/learnerprofiles">
                <i class="fas fa-id-card"></i> <span>Learner Profiles</span>
            </a>
        </Authorized>
    </AuthorizeView>

    <AuthorizeView Roles="Learner" Context="learnerContext">
        <Authorized>
            <div class="nav-section-label">Learner</div>
            <a class="@NavClass("/learner/dashboard")" href="/learner/dashboard">
                <i class="fas fa-chart-line"></i> <span>Dashboard</span>
            </a>
            <a class="@NavClass("/learner/marks")" href="/learner/marks">
                <i class="fas fa-chart-pie"></i> <span>Marks</span>
            </a>
            <a class="@NavClass("/learner/attendance")" href="/learner/attendance">
                <i class="fas fa-calendar-check"></i> <span>Attendance</span>
            </a>
        </Authorized>
    </AuthorizeView>
</nav>

@code {
    private string currentPath = "";

    protected override void OnInitialized()
    {
        currentPath = "/" + NavigationManager.ToBaseRelativePath(NavigationManager.Uri).Split('?')[0].TrimEnd('/');
        NavigationManager.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        currentPath = "/" + NavigationManager.ToBaseRelativePath(e.Location).Split('?')[0].TrimEnd('/');
        StateHasChanged();
    }

    private string NavClass(string href)
    {
        var normalizedHref = href.TrimEnd('/');
        return currentPath.Equals(normalizedHref, StringComparison.OrdinalIgnoreCase)
            ? "nav-item active"
            : "nav-item";
    }

    public void Dispose()
    {
        NavigationManager.LocationChanged -= OnLocationChanged;
    }
}
```

**Important — why this will actually stay highlighted this time:** because `MainLayout`
and `NavMenu` have no render mode of their own, every single page navigation causes the
whole layout (including this component) to be freshly re-evaluated on the server, with
`OnInitialized()` running again and picking up the current URL correctly. There's no
stale, cached circuit state to fight against anymore — this was the actual root cause of
the highlight not sticking before, not a flaw in this logic.

---

## STEP 7 — `aris-styles.css` additions: active nav border + fonts already covered in Step 1

**Go to `wwwroot/aris-styles.css`.** If this is a fresh copy of the same file you've used
before, the `.nav-item.active` rule should already read:
```css
.nav-item.active {
    background: var(--blue-light);
    color: var(--blue);
    border: 1.5px solid var(--blue);
}
```
If it's still the older solid-fill version, update it to the above.

---

## STEP 8 — Fix the login redirect to honor where you were headed

**Go to `Components/Account/Pages/Login.razor`, in the `@code` block. Find:**
```csharp
            if (result.Succeeded)
            {
                Logger.LogInformation("User logged in.");
                RedirectManager.RedirectTo("/");
            }
```
**Replace with:**
```csharp
            if (result.Succeeded)
            {
                Logger.LogInformation("User logged in.");
                RedirectManager.RedirectTo(string.IsNullOrEmpty(ReturnUrl) ? "/" : ReturnUrl);
            }
```

---

## STEP 9 — Rebuild, hard refresh, test

1. Rebuild the solution
2. Hard refresh (Ctrl+F5) — `aris-styles.css` isn't fingerprinted, so old cached copies
   can hide your changes
3. Check off each of these:

- [ ] Login page shows the card/gradient design
- [ ] Sidebar is white, fixed, content starts immediately to its right, no dividing line under the header
- [ ] Clicking the ARIS logo collapses the sidebar to icons-only, smoothly, and it's still collapsed after navigating to another page
- [ ] Header shows the current page name and the person's role underneath — no page name duplicated in the body
- [ ] Whichever sidebar item matches your current page has a blue border around it
- [ ] "My Profile" is not in the Learner sidebar
- [ ] Header avatar → Edit Profile and Change Password both open the real pages, not "Not Found"
- [ ] Logging in after being redirected to Login sends you back to where you were headed, not always to your dashboard

If any single box doesn't check out, tell me which one specifically — with this clean
rebuild, we should be down to isolated, easy-to-diagnose issues rather than the tangled
render-mode conflicts from before.
