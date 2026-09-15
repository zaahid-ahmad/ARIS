# ARIS Resources & Programs - Complete Implementation Specification for Claude

## Overview

This document provides a complete specification for implementing *Resources* and *Programs* modules in the ARIS system (Blazor + C#). These modules are designed to integrate with the existing risk analysis and intervention workflow.

---

## 1. RESOURCES MODULE

### 1.1 Purpose
Teachers upload and manage learning materials. Learners view and access resources. Resources are organized by *Subject* and *Grade* using a card-based navigation system.

### 1.2 Navigation Flow

#### Teacher Flow:

Resources Tab
    │
    ├── Subject Grid View (default)
    │   └── Each subject card shows:
    │       ├── Subject Name
    │       ├── Grade
    │       └── Resource Count badge
    │
    └── Click Subject Card → Subject Detail View
        ├── Header: Subject name + Grade + "Add Resource" button
        ├── Search bar (filters resources in current subject)
        └── Resource List:
            ├── Resource Title
            ├── Description
            ├── Tags (Category, Term, Type)
            └── Actions: Download/Open + Edit + Delete


#### Learner Flow:

My Resources Tab
    │
    ├── Subject Grid View (default)
    │   └── Shows only enrolled subjects
    │
    └── Click Subject Card → Subject Detail View
        ├── Resource List (read-only)
        └── Actions: Download/Open only


### 1.3 Data Models

csharp
// Resource.cs
public class Resource
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "PDF/Document"; // PDF/Document, Video, External Link, school-based
    public bool IsActive { get; set; } = true;
    public string? Category { get; set; } // e.g., Algebra, Chemistry, Study Skills
    public string? Term { get; set; } // Term 1, Term 2, Term 3, Term 4, All Year
    public int? SubjectId { get; set; }
    public string? FileUrl { get; set; } // For uploaded files
    public string? ExternalUrl { get; set; } // For external links
    public int UploadedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual Subject? Subject { get; set; }
    public virtual User? UploadedBy { get; set; }
}

// ResourceType Enum
public enum ResourceType
{
    PDFDocument,
    Video,
    ExternalLink,
    SchoolBased
}


### 1.4 Key Implementation Details

#### 1.4.1 Subject Grid Navigation (NO REDUNDANCY)

The resource view uses a *two-level navigation*:

*Level 1 - Subject Grid*:
- Shows all subjects the teacher teaches (or learner is enrolled in)
- Each card shows: Subject Name, Grade, Resource Count
- Click card → Navigate to Level 2

*Level 2 - Subject Detail*:
- Shows resources for the selected subject ONLY
- No grade filter needed (you're already in the subject)
- "Add Resource" button pre-fills the subject

razor
@* TeacherResources.razor - Subject Grid *@
@if (SelectedSubjectId == null)
{
    <div class="subject-grid">
        @foreach (var subject in TeacherSubjects)
        {
            var count = Resources.Count(r => r.SubjectId == subject.Id && r.IsActive);
            <div class="subject-card" @onclick="() => SelectSubject(subject.Id)">
                <div class="icon"><i class="fas fa-folder-open"></i></div>
                <div class="name">@subject.Name</div>
                <div class="meta">Grade @subject.Grade</div>
                <div class="badge">@count resources</div>
            </div>
        }
    </div>
}
else
{
    @* Subject Detail View *@
    <button @onclick="() => SelectedSubjectId = null">← Back</button>
    <h3>@SelectedSubject.Name <span class="badge">G@SelectedSubject.Grade</span></h3>
    <button @onclick="OpenAddResourceModal(SelectedSubject.Id)">Add Resource</button>
    @* Resource list for this subject *@
}


#### 1.4.2 File Upload Flow (Sequential)

When uploading a resource:
1. User selects *Resource Type* first
2. Based on type, show/hide relevant fields:
   - *PDF/Document, Video, school-based*: Show File Upload
   - *External Link*: Show URL input

csharp
// In AddResourceModal.razor
private void OnResourceTypeChanged(ChangeEventArgs e)
{
    var selectedType = e.Value?.ToString();
    ShowFileUpload = selectedType != "ExternalLink";
    ShowExternalUrl = selectedType == "ExternalLink";
    
    // Clear the other field when switching
    if (ShowFileUpload) ExternalUrl = string.Empty;
    if (ShowExternalUrl) FileUpload = null;
}


razor
<div class="modal-field">
    <label>Resource Type *</label>
    <select @bind="NewResource.Type" @onchange="OnResourceTypeChanged">
        <option value="PDFDocument">PDF/Document</option>
        <option value="Video">Video</option>
        <option value="ExternalLink">External Link</option>
        <option value="SchoolBased">School-based</option>
    </select>
</div>

@if (ShowFileUpload)
{
    <div class="modal-field">
        <label>File Upload</label>
        <InputFile OnChange="OnFileSelected" accept=".pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.mp4,.mov" />
        @if (SelectedFile != null)
        {
            <small>Selected: @SelectedFile.Name</small>
        }
    </div>
}

@if (ShowExternalUrl)
{
    <div class="modal-field">
        <label>External URL</label>
        <InputText @bind-Value="NewResource.ExternalUrl" placeholder="https://example.com/resource" />
    </div>
}


---

## 2. PROGRAMS MODULE

### 2.1 Purpose
School Admin manages a directory of programs. Teachers can recommend programs during interventions. Learners can request programs.

### 2.2 User Roles & Capabilities

| Role | View | Request | Manage |
|------|------|---------|--------|
| *School Admin* | ✅ Full list | ❌ | ✅ CRUD + Activate/Deactivate |
| *Teacher* | ✅ Full list (active only) | ❌ | ❌ (can recommend via intervention) |
| *Learner* | ✅ Full list (active only) | ✅ Request program | ❌ |

### 2.3 Data Models

csharp
// Program.cs
public class Program
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "Internal"; // Internal, External
    public string Category { get; set; } = "Academic"; // Sports, Academic, Arts, Other
    public string Provider { get; set; } = string.Empty;
    public string? TargetAudience { get; set; } // e.g., "Grades 10-12"
    public string? GradeLevel { get; set; } // e.g., "10,11,12"
    public string? ContactInfo { get; set; } // Email or phone
    public string? WebLink { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

// ProgramCategory Enum
public enum ProgramCategory
{
    Academic,
    Sports,
    Arts,
    Career,
    Wellness,
    Other
}

// ProgramRequest.cs (Learner-initiated)
public class ProgramRequest
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    public int ProgramId { get; set; }
    public string Status { get; set; } = "pending"; // pending, approved, rejected
    public string? Reason { get; set; }
    public DateTime RequestDate { get; set; }
    public string? AdminNotes { get; set; }
    
    // Navigation
    public virtual User? Learner { get; set; }
    public virtual Program? Program { get; set; }
}


### 2.4 Program Categories

Categories are predefined but extendable:
- *Academic* - Tutoring, study skills, subject support
- *Sports* - Athletics, team sports, fitness programs
- *Arts* - Drama, music, visual arts, dance
- *Career* - Career guidance, job shadowing, internships
- *Wellness* - Mental health, counselling, peer support
- *Other* - General programs

Admins can add new categories via a text input in the Add/Edit modal.

razor
@* Program Categories - Admin Add/Edit *@
<div class="modal-field">
    <label>Category</label>
    <select @bind="EditingProgram.Category">
        <option value="">Select Category</option>
        @foreach (var cat in AvailableCategories)
        {
            <option value="@cat">@cat</option>
        }
        <option value="__new__">+ Add New Category</option>
    </select>
    @if (EditingProgram.Category == "__new__")
    {
        <input type="text" @bind="NewCategoryName" placeholder="Enter new category name..." />
    }
</div>


### 2.5 Learner Program Requests

Learners can request to join a program from the Programs view:

razor
@* LearnerPrograms.razor - Request Button *@
@if (IsLearner)
{
    <button class="btn btn-primary" @onclick="() => OpenRequestModal(program)">
        <i class="fas fa-hand-paper"></i> Request to Join
    </button>
}

@* Request Modal *@
<div class="modal">
    <h3>Request to Join @program.Name</h3>
    <p>@program.Description</p>
    <div class="modal-field">
        <label>Reason for joining</label>
        <textarea @bind="RequestReason" placeholder="Why do you want to join this program?"></textarea>
    </div>
    <button @onclick="SubmitProgramRequest">Submit Request</button>
</div>


Admin sees pending requests in Approvals tab:

csharp
// In Approvals.razor
@if (SelectedApprovalTab == "programRequests")
{
    @foreach (var request in PendingProgramRequests)
    {
        <div class="approval-card">
            <div>
                <strong>@request.Learner?.Name</strong> requested 
                <strong>@request.Program?.Name</strong>
                <div class="text-muted">Reason: @request.Reason</div>
            </div>
            <div>
                <button @onclick="() => ApproveProgramRequest(request.Id)">Approve</button>
                <button @onclick="() => RejectProgramRequest(request.Id)">Reject</button>
            </div>
        </div>
    }
}


---

## 3. INTERVENTIONS (Risk Analysis Integration)

### 3.1 The Intervention Flow

The *Risk Analysis* tab shows at-risk learners. Each learner has an *"Intervene"* button that opens a modal with three options:


Risk Analysis Tab
    │
    └── At-Risk Learner List
        │
        └── Click "Intervene" button
            │
            ├── Option 1: Recommend Resource
            │   ├── Select from Resources list
            │   ├── Add notes
            │   └── Submit → Admin approval
            │
            ├── Option 2: Schedule Consultation
            │   ├── Enter topic
            │   ├── Add notes
            │   └── Submit → Direct notification to learner
            │
            └── Option 3: Recommend Program
                ├── Select from Programs list
                ├── Add notes
                └── Submit → Admin approval


### 3.2 Data Model

csharp
// Intervention.cs
public class Intervention
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public int LearnerId { get; set; }
    public int? SubjectId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
    public string Status { get; set; } = "pending_admin"; // pending_admin, active, rejected
    public string Type { get; set; } = "resource"; // resource, consultation, program
    public DateTime CreatedAt { get; set; }
    
    // For program recommendations
    public int? ProgramId { get; set; }
    public int? ResourceId { get; set; }
    
    // Navigation
    public virtual User? Teacher { get; set; }
    public virtual User? Learner { get; set; }
    public virtual Subject? Subject { get; set; }
    public virtual Program? Program { get; set; }
    public virtual Resource? Resource { get; set; }
}


### 3.3 Intervention Modal Code

razor
@* InterventionModal.razor *@
<div class="interv-options">
    <div class="interv-option @(SelectedType == "resource" ? "selected" : "")" 
         @onclick="() => SelectType("resource")">
        <div class="icon"><i class="fas fa-book"></i></div>
        <div class="label">Recommend Resource</div>
        <div class="sub">Submit to Admin for approval</div>
    </div>
    <div class="interv-option @(SelectedType == "consultation" ? "selected" : "")" 
         @onclick="() => SelectType("consultation")">
        <div class="icon"><i class="fas fa-comments"></i></div>
        <div class="label">Schedule Consultation</div>
        <div class="sub">Direct support session</div>
    </div>
    <div class="interv-option @(SelectedType == "program" ? "selected" : "")" 
         @onclick="() => SelectType("program")">
        <div class="icon"><i class="fas fa-clipboard-list"></i></div>
        <div class="label">Recommend Program</div>
        <div class="sub">Suggest from Programs Directory</div>
    </div>
</div>

@if (SelectedType == "resource")
{
    <div class="modal-field">
        <label>Resource</label>
        <select @bind="SelectedResourceId">
            @foreach (var resource in AvailableResources)
            {
                <option value="@resource.Id">@resource.Title</option>
            }
        </select>
    </div>
}

@if (SelectedType == "program")
{
    <div class="modal-field">
        <label>Program</label>
        <select @bind="SelectedProgramId">
            @foreach (var program in AvailablePrograms)
            {
                <option value="@program.Id">@program.Name</option>
            }
        </select>
    </div>
}

@if (SelectedType == "consultation")
{
    <div class="modal-field">
        <label>Topic</label>
        <input @bind="ConsultationTopic" placeholder="e.g. Grade 11 Maths Review" />
    </div>
}

<div class="modal-field">
    <label>Notes</label>
    <textarea @bind="InterventionNotes" placeholder="Describe the reason for intervention..."></textarea>
</div>

<button @onclick="SubmitIntervention">Submit @(SelectedType == "consultation" ? "Consultation" : "Recommendation")</button>


### 3.4 Intervention Submission Logic

csharp
private async Task SubmitIntervention()
{
    var intervention = new Intervention
    {
        TeacherId = CurrentUserId,
        LearnerId = SelectedLearnerId,
        SubjectId = SelectedSubjectId,
        Topic = SelectedType == "consultation" ? ConsultationTopic : 
                SelectedType == "resource" ? "Resource Recommendation" : "Program Recommendation",
        Message = InterventionNotes,
        Recommendation = SelectedType == "resource" ? GetResourceName(SelectedResourceId) :
                         SelectedType == "program" ? GetProgramName(SelectedProgramId) : "Consultation",
        Status = SelectedType == "consultation" ? "active" : "pending_admin",
        Type = SelectedType,
        CreatedAt = DateTime.UtcNow,
        ProgramId = SelectedType == "program" ? SelectedProgramId : null,
        ResourceId = SelectedType == "resource" ? SelectedResourceId : null
    };
    
    await InterventionService.AddAsync(intervention);
    
    // Notify admin for resource/program recommendations
    if (SelectedType != "consultation")
    {
        await NotificationService.NotifyAdmin(intervention);
    }
    
    // Notify learner for consultations
    if (SelectedType == "consultation")
    {
        await NotificationService.NotifyLearner(intervention);
    }
    
    Dialog.Close(true);
}


---

## 4. NOTIFICATIONS & APPROVALS

### 4.1 Notification Types

| Event | Trigger | Recipient | Action Required |
|-------|---------|-----------|-----------------|
| *Resource Recommended* | Teacher submits intervention | School Admin | Approve/Reject |
| *Program Recommended* | Teacher submits intervention | School Admin | Approve/Reject |
| *Program Requested* | Learner requests program | School Admin | Approve/Reject |
| *Consultation Scheduled* | Teacher schedules consultation | Learner | Acknowledge |
| *Approval Granted* | Admin approves | Teacher | Follow up |

### 4.2 Admin Approvals Tab

The Approvals tab shows three types of pending items:

razor
@* Approvals.razor *@
<div class="tab-row">
    <button class="tab-btn @(ApprovalTab == "interventions" ? "active" : "")" 
            @onclick="() => ApprovalTab = "interventions"">
        Interventions (@PendingInterventions.Count)
    </button>
    <button class="tab-btn @(ApprovalTab == "programRequests" ? "active" : "")" 
            @onclick="() => ApprovalTab = "programRequests"">
        Program Requests (@PendingProgramRequests.Count)
    </button>
</div>

@if (ApprovalTab == "interventions")
{
    @foreach (var intervention in PendingInterventions)
    {
        <div class="approval-card">
            <div>
                <strong>@intervention.Learner?.Name</strong>
                <span class="badge">@intervention.Type</span>
                <div>@intervention.Message</div>
                <div class="text-muted">Recommended by @intervention.Teacher?.Name</div>
            </div>
            <div>
                <button @onclick="() => ApproveIntervention(intervention.Id)">Approve</button>
                <button @onclick="() => RejectIntervention(intervention.Id)">Reject</button>
            </div>
        </div>
    }
}

@if (ApprovalTab == "programRequests")
{
    @foreach (var request in PendingProgramRequests)
    {
        <div class="approval-card">
            <div>
                <strong>@request.Learner?.Name</strong> wants to join
                <strong>@request.Program?.Name</strong>
                <div class="text-muted">Reason: @request.Reason</div>
            </div>
            <div>
                <button @onclick="() => ApproveProgramRequest(request.Id)">Approve</button>
                <button @onclick="() => RejectProgramRequest(request.Id)">Reject</button>
            </div>
        </div>
    }
}


---

## 5. COMPLETE FILE STRUCTURE FOR BLAZOR


/Pages/
├── Resources/
│   ├── TeacherResources.razor          # Teacher resource management
│   ├── TeacherResources.razor.cs       # Logic
│   ├── LearnerResources.razor          # Learner resource view
│   ├── LearnerResources.razor.cs       # Logic
│   ├── AddResourceModal.razor          # Upload modal
│   └── EditResourceModal.razor         # Edit modal
│
├── Programs/
│   ├── ProgramsDirectory.razor         # Admin management
│   ├── ProgramsDirectory.razor.cs      # Logic
│   ├── LearnerPrograms.razor           # Learner view with requests
│   ├── LearnerPrograms.razor.cs        # Logic
│   ├── AddProgramModal.razor           # Add program modal
│   └── EditProgramModal.razor          # Edit program modal
│
├── Interventions/
│   ├── InterventionModal.razor         # Reusable intervention modal
│   ├── InterventionModal.razor.cs      # Logic
│   └── RiskAnalysis.razor              # Risk tab with intervention buttons
│
├── Approvals/
│   └── Approvals.razor                 # Admin approvals with tabs
│
└── Shared/
    ├── SubjectCard.razor               # Reusable subject card component
    ├── ResourceCard.razor              # Reusable resource card
    └── ProgramCard.razor               # Reusable program card

/Services/
├── IResourceService.cs
├── ResourceService.cs
├── IProgramService.cs
├── ProgramService.cs
├── IInterventionService.cs
├── InterventionService.cs
├── INotificationService.cs
└── NotificationService.cs

/Models/
├── Resource.cs
├── Program.cs
├── ProgramRequest.cs
├── Intervention.cs
└── Enums/
    ├── ResourceType.cs
    ├── ProgramCategory.cs
    └── InterventionType.cs

/Data/
└── ApplicationDbContext.cs             # EF Core migrations


---

## 6. DATABASE MIGRATIONS (EF Core)

csharp
// Migration for Resources, Programs, ProgramRequests, Interventions
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Resources table
    migrationBuilder.CreateTable(
        name: "Resources",
        columns: table => new
        {
            Id = table.Column<int>(nullable: false)
                .Annotation("Sqlite:Autoincrement", true),
            Title = table.Column<string>(nullable: false),
            Description = table.Column<string>(nullable: false),
            Type = table.Column<string>(nullable: false),
            IsActive = table.Column<bool>(nullable: false),
            Category = table.Column<string>(nullable: true),
            Term = table.Column<string>(nullable: true),
            SubjectId = table.Column<int>(nullable: true),
            FileUrl = table.Column<string>(nullable: true),
            ExternalUrl = table.Column<string>(nullable: true),
            UploadedByUserId = table.Column<int>(nullable: false),
            CreatedAt = table.Column<DateTime>(nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_Resources", x => x.Id);
            table.ForeignKey(
                name: "FK_Resources_Subjects_SubjectId",
                column: x => x.SubjectId,
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
            table.ForeignKey(
                name: "FK_Resources_Users_UploadedByUserId",
                column: x => x.UploadedByUserId,
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        });

    // Programs table
    migrationBuilder.CreateTable(
        name: "Programs",
        columns: table => new
        {
            Id = table.Column<int>(nullable: false)
                .Annotation("Sqlite:Autoincrement", true),
            Name = table.Column<string>(nullable: false),
            Description = table.Column<string>(nullable: false),
            Type = table.Column<string>(nullable: false),
            Category = table.Column<string>(nullable: false),
            Provider = table.Column<string>(nullable: false),
            TargetAudience = table.Column<string>(nullable: true),
            GradeLevel = table.Column<string>(nullable: true),
            ContactInfo = table.Column<string>(nullable: true),
            WebLink = table.Column<string>(nullable: true),
            IsActive = table.Column<bool>(nullable: false),
            CreatedAt = table.Column<DateTime>(nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_Programs", x => x.Id);
        });

    // ProgramRequests table
    migrationBuilder.CreateTable(
        name: "ProgramRequests",
        columns: table => new
        {
            Id = table.Column<int>(nullable: false)
                .Annotation("Sqlite:Autoincrement", true),
            LearnerId = table.Column<int>(nullable: false),
            ProgramId = table.Column<int>(nullable: false),
            Status = table.Column<string>(nullable: false),
            Reason = table.Column<string>(nullable: true),
            RequestDate = table.Column<DateTime>(nullable: false),
            AdminNotes = table.Column<string>(nullable: true)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_ProgramRequests", x => x.Id);
            table.ForeignKey(
                name: "FK_ProgramRequests_Users_LearnerId",
                column: x => x.LearnerId,
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
            table.ForeignKey(
                name: "FK_ProgramRequests_Programs_ProgramId",
                column: x => x.ProgramId,
                principalTable: "Programs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        });

    // Add ProgramId and ResourceId to Interventions
    migrationBuilder.AddColumn<int>(
        name: "ProgramId",
        table: "Interventions",
        nullable: true);

    migrationBuilder.AddColumn<int>(
        name: "ResourceId",
        table: "Interventions",
        nullable: true);

    migrationBuilder.AddColumn<string>(
        name: "Type",
        table: "Interventions",
        nullable: false,
        defaultValue: "resource");

    migrationBuilder.CreateIndex(
        name: "IX_Resources_SubjectId",
        table: "Resources",
        column: "SubjectId");

    migrationBuilder.CreateIndex(
        name: "IX_ProgramRequests_LearnerId",
        table: "ProgramRequests",
        column: "LearnerId");

    migrationBuilder.CreateIndex(
        name: "IX_ProgramRequests_ProgramId",
        table: "ProgramRequests",
        column: "ProgramId");
}


---

## 7. SIDEBAR NAVIGATION UPDATES

csharp
// In NavMenu.razor or Layout

// Teacher
public List<NavItem> GetTeacherNavItems()
{
    return new List<NavItem>
    {
        new() { Id = "dashboard", Icon = "fa-chart-line", Label = "Dashboard" },
        new() { Id = "subjects", Icon = "fa-sitemap", Label = "Subject Structure" },
        new() { Id = "marks", Icon = "fa-pen-ruler", Label = "Mark Entry" },
        new() { Id = "analysis", Icon = "fa-chart-line", Label = "Risk Analysis" },
        new() { Id = "attendance", Icon = "fa-calendar-check", Label = "Attendance" },
        new() { Id = "resources", Icon = "fa-folder-open", Label = "Resources" },
        new() { Id = "interventions", Icon = "fa-hand-holding-heart", Label = "Interventions" }
    };
}

// Learner
public List<NavItem> GetLearnerNavItems()
{
    return new List<NavItem>
    {
        new() { Id = "dashboard", Icon = "fa-chart-line", Label = "Dashboard" },
        new() { Id = "subjects", Icon = "fa-sitemap", Label = "My Subjects" },
        new() { Id = "performance", Icon = "fa-chart-pie", Label = "My Performance" },
        new() { Id = "consultations", Icon = "fa-comments", Label = "Consultations" },
        new() { Id = "resources", Icon = "fa-folder-open", Label = "My Resources" },
        new() { Id = "programs", Icon = "fa-clipboard-list", Label = "Programs" }
    };
}

// School Admin
public List<NavItem> GetAdminNavItems()
{
    return new List<NavItem>
    {
        new() { Id = "dashboard", Icon = "fa-chart-line", Label = "Dashboard" },
        new() { Id = "users", Icon = "fa-users", Label = "User Management" },
        new() { Id = "subjects", Icon = "fa-sitemap", Label = "Subject Structure" },
        new() { Id = "approvals", Icon = "fa-check-circle", Label = "Approvals", Badge = PendingCount },
        new() { Id = "analytics", Icon = "fa-chart-pie", Label = "School Analytics" },
        new() { Id = "resources", Icon = "fa-folder-open", Label = "Resource Desk" },
        new() { Id = "programs", Icon = "fa-clipboard-list", Label = "Programs Directory" }
    };
}


---

## 8. KEY INTERACTIONS & WORKFLOWS

### 8.1 Resource Upload Flow (Teacher)
1. Teacher navigates to Resources → Subject Grid
2. Clicks subject card → Subject Detail View
3. Clicks "Add Resource"
4. Modal opens with:
   - Title, Description, Subject (pre-filled), Term, Category
   - Resource Type dropdown (PDF, Video, External Link, School-based)
   - File upload OR URL input (based on type)
5. Submits → Resource saved to database

### 8.2 Program Request Flow (Learner)
1. Learner navigates to Programs
2. Views program cards
3. Clicks "Request to Join" on a program
4. Modal opens with reason textarea
5. Submits → ProgramRequest created (pending)
6. Admin sees in Approvals tab → Approves/Rejects

### 8.3 Intervention Flow (Teacher → Admin)
1. Teacher in Risk Analysis tab
2. Sees at-risk learner → Clicks "Intervene"
3. Selects one of three options
4. Fills in details + notes
5. Submits → Intervention created
6. Admin sees in Approvals tab
7. Admin Approves/Rejects
8. If approved → Status becomes "active"
9. Learner/Teacher notified

---

## 9. SUMMARY OF FIXES & IMPROVEMENTS

| Issue | Fix |
|-------|-----|
| Resource Desk on Admin | Removed - Admin only views resources via Resource Desk |
| Redundancy in subject selection | Two-level navigation: Subject Grid → Subject Detail (no extra grade filter) |
| File type vs File upload sequencing | Resource Type first, then show relevant field only |
| Learner program requests | Added ProgramRequest model + UI for learners to request |
| Program categories | Added Category field with predefined + custom options |
| Approvals missing | Detailed Approvals tab with Interventions + Program Requests |
| Risk analysis integration | Intervention Modal integrated with Risk Analysis → 3 options |

---

## 10. READY FOR CLAUDE

This document contains:
- ✅ Complete data models
- ✅ Navigation structure
- ✅ UI component structure
- ✅ Code examples (Razor + C#)
- ✅ Database migrations
- ✅ Workflow descriptions
- ✅ All user role capabilities

Send this to Claude with the prompt:
> "Implement the Resources and Programs modules for ARIS using this specification. Use Blazor with EF Core, following the structure described above."