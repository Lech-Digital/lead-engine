using Coldstart.Application;
using Coldstart.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Coldstart")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Coldstart"));
builder.Services.AddHttpTenantResolution();

builder.Services.Configure<Coldstart.Infrastructure.Email.SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<Coldstart.Infrastructure.Mailbox.MailboxOptions>(builder.Configuration.GetSection("Mailbox"));
builder.Services.Configure<Coldstart.Infrastructure.Harvest.LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.Configure<Coldstart.Infrastructure.Outreach.OutreachMailboxOptions>(builder.Configuration.GetSection("OutreachMailbox"));
builder.Services.Configure<Coldstart.Infrastructure.Harvest.HarvestScheduleOptions>(builder.Configuration.GetSection("HarvestSchedule"));

builder.Services.AddSingleton(_ =>
{
    var n = new Coldstart.Application.Harvest.NicheOptions();
    builder.Configuration.GetSection("Niche").Bind(n);
    return n;
});
builder.Services.AddSingleton(_ =>
{
    var q = new Coldstart.Application.Leads.QuotaOptions();
    builder.Configuration.GetSection("Quota").Bind(q);
    return q;
});
builder.Services.AddHarvestQueue();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o => { o.SignIn.RequireConfirmedAccount = false; })
    .AddEntityFrameworkStores<ColdstartDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Identity/Account/Login";
    o.LogoutPath = "/Identity/Account/Logout";
    o.AccessDeniedPath = "/Identity/Account/Login";
});

builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(7));

// In production, persist Data Protection keys to a mounted volume so invite/reset
// tokens and auth cookies survive container redeploys. No-op in dev (unset).
var dpKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dpKeyPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dpKeyPath));

var authBuilder = builder.Services.AddAuthentication();
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret))
{
    authBuilder.AddGoogle(o =>
    {
        o.ClientId = googleId;
        o.ClientSecret = googleSecret;
    });
}

var app = builder.Build();

// Dev convenience: apply migrations + seed on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, CancellationToken.None);
    await Coldstart.Web.IdentitySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.Run();

public partial class Program; // for WebApplicationFactory
