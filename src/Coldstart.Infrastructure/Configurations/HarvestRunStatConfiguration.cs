using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class HarvestRunStatConfiguration : IEntityTypeConfiguration<HarvestRunStat>
{
    public void Configure(EntityTypeBuilder<HarvestRunStat> builder)
    {
        builder.ToTable("harvest_run_stats");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.RanAt);
        // Cross-tenant aggregate telemetry — deliberately no tenant filter, no lead content.
    }
}
