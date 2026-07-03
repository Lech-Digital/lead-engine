using Coldstart.Application;
using Coldstart.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Coldstart")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Coldstart"));

var app = builder.Build();

// Dev convenience: apply migrations + seed on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, CancellationToken.None);
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

public partial class Program; // for WebApplicationFactory
