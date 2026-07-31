using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class SuppressionConfiguration : IEntityTypeConfiguration<Suppression>
{
    public void Configure(EntityTypeBuilder<Suppression> builder)
    {
        builder.ToTable("suppressions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Value).HasMaxLength(200).IsRequired();
        builder.HasIndex(s => s.Value).IsUnique();
        builder.Property(s => s.Note).HasMaxLength(500);
        // Deliberately NO tenant filter: an objection excludes a company from ALL processing.
    }
}
