using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Coldstart.Infrastructure;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ColdstartDbContext>
{
    public ColdstartDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ColdstartDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=coldstart;Username=coldstart;Password=localdev")
            .Options;
        return new ColdstartDbContext(options);
    }
}
