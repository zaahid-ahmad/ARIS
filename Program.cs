using ARIS1.Components;
using ARIS1.Components.Account;
using ARIS1.Data;
using ARIS1.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using ARIS1.Services;

//Testing new branch

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<WeightingService>();

builder.Services.AddScoped<InterventionService>();

builder.Services.AddScoped<WeightCalculationService>();
builder.Services.AddScoped<SchoolAuthorizationService>();
builder.Services.AddScoped<RiskAssessmentService>();
builder.Services.AddScoped<BulkUserImportService>();
builder.Services.AddScoped<BulkSubjectAllocationService>();
builder.Services.AddScoped<YearRolloverService>();

// Support.razor depends only on IChatAssistantService — uses an OpenAI-compatible LLM
// (Groq by default) when an API key is configured, otherwise the free rule-based bot.
// The rule-based bot is always registered since the LLM service falls back to it on failure.
// dotnet user-secrets set "ChatAssistant:ApiKey" "<groq-key>"
// Optional: ChatAssistant:BaseUrl (default https://api.groq.com/openai/v1), ChatAssistant:Model.
builder.Services.AddScoped<RuleBasedChatAssistantService>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["ChatAssistant:ApiKey"]))
{
    builder.Services.AddHttpClient<OpenAiCompatibleChatAssistantService>(c => c.Timeout = TimeSpan.FromSeconds(20));
    builder.Services.AddScoped<IChatAssistantService>(sp => sp.GetRequiredService<OpenAiCompatibleChatAssistantService>());
}
else
{
    builder.Services.AddScoped<IChatAssistantService, RuleBasedChatAssistantService>();
}

// Add Database Context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Identity
builder.Services.AddIdentityCore<User>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredUniqueChars = 4;
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager()
.AddClaimsPrincipalFactory<ARIS1.Services.AppUserClaimsPrincipalFactory>()
.AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();

builder.Services.AddAuthorization();

// Add Razor components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Default SignalR message size (32KB) is too small for bulk-import CSV/Excel uploads via InputFile
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Email: pages queue EmailLog rows via EmailService; EmailBackgroundService sends them over SMTP (MailKit).
// Configure with user-secrets (Email:Host, Email:Port, Email:Username, Email:Password, Email:FromAddress,
// Email:FromName, Email:RedirectAllTo). Without Email:Host, emails are logged as Skipped instead of sent.
builder.Services.Configure<ARIS1.Services.Email.EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<ARIS1.Services.Email.EmailQueue>();
builder.Services.AddHostedService<ARIS1.Services.Email.EmailBackgroundService>();
builder.Services.AddScoped<ARIS1.Services.Email.EmailService>();
builder.Services.AddScoped<ARIS1.Services.Email.AccountEmailService>();
builder.Services.AddScoped<ARIS1.Services.Email.ParentRecipientService>();
builder.Services.AddScoped<ARIS1.Services.Email.ParentAlertService>();
builder.Services.AddScoped<ARIS1.Services.Email.ProgressSummaryService>();
builder.Services.AddScoped<IEmailSender<User>, ARIS1.Services.Email.IdentityEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

// Seed roles and admin account
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();   // build the DB on a clean machine before seeding
    await DbSeeder.SeedAsync(scope.ServiceProvider, builder.Configuration);

    if (args.Contains("--seed-demo"))
    {
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider);
    }
}

app.Run();