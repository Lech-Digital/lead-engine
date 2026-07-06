using Coldstart.Application;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Harvest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Pin the content root to the built output directory (where appsettings.json is
// copied) rather than the process's current working directory. Without this,
// `dotnet run --project src/Coldstart.Harvester` invoked from the repo root fails
// to find appsettings.json because HostApplicationBuilder defaults ContentRootPath
// to Directory.GetCurrentDirectory().
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddUserSecrets<Program>(optional: true);

var conn = builder.Configuration.GetConnectionString("Coldstart")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Coldstart");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(conn);
builder.Services.AddSingleton(sp =>
{
    var n = new NicheOptions();
    sp.GetRequiredService<IConfiguration>().GetSection("Niche").Bind(n);
    return n;
});
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var harvest = scope.ServiceProvider.GetRequiredService<IHarvestLeads>();
var report = await harvest.RunAsync(CancellationToken.None);
Console.WriteLine($"harvested={report.Harvested} scored_in={report.ScoredIn} deduped={report.Deduped} " +
                  $"enriched={report.Enriched} saved={report.Saved}");
