using Microsoft.AspNetCore.Hosting;

namespace Coldstart.Web.Tests;

// A factory with a SMALL free quota so the foil actually engages (the shared factory sets it huge).
public sealed class FoilWebFactory : ColdstartWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Quota:FreeLeadCount", "2");   // overrides the base's huge value (last wins)
    }
}
