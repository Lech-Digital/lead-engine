using Coldstart.Application;
using Coldstart.Infrastructure;
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

builder.Services.Configure<Coldstart.Infrastructure.Email.SmtpOptions>(builder.Configuration.GetSection("Smtp"));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o => { o.SignIn.RequireConfirmedAccount = false; })
    .AddEntityFrameworkStores<ColdstartDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Identity/Account/Login";
    o.LogoutPath = "/Identity/Account/Logout";
    o.AccessDeniedPath = "/Identity/Account/Login";
});

builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(7));

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
