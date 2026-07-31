using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasConversion(id => id.Value, value => new LeadId(value))
            .ValueGeneratedNever();
        builder.Property(l => l.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value));
        builder.Property(l => l.Company).IsRequired();
        builder.Property(l => l.Subject).IsRequired();
        builder.Property(l => l.Draft).IsRequired();
        builder.Property(l => l.Fit).HasConversion<string>();
        builder.Property(l => l.Status).HasConversion<string>();
        builder.Property(l => l.EmailConfidence).HasConversion<string>();
        builder.Property(l => l.Outcome).HasConversion<string>().HasMaxLength(16).HasDefaultValue(LeadOutcome.None);
        builder.Property(l => l.SkipReason).HasMaxLength(120);
        builder.Property(l => l.RecipientEmail)
            .HasConversion(
                email => email!.Value,
                value => new EmailAddress(value));
        builder.Property(l => l.CreatedAt);
        builder.Property(l => l.SentAt);
        builder.Property(l => l.Source);
        builder.Property(l => l.SourceUrl);
        builder.Property(l => l.Fingerprint);
        builder.Property(l => l.Score);
        builder.Property(l => l.MatchedSignals);
        builder.HasIndex(l => new { l.CustomerId, l.Fingerprint })
            .IsUnique()
            .HasFilter("\"Fingerprint\" <> ''");   // seeded/mock leads have '' — don't collide; dedup is per-tenant
        builder.HasIndex(l => l.CustomerId);   // covers tenant-scoped reads (global query filter); partial composite above can't serve these

        builder.OwnsMany(l => l.Notes, n =>
        {
            n.ToTable("lead_notes");
            n.WithOwner().HasForeignKey("LeadId");
            n.HasKey("Id");
            n.Property(x => x.Id).ValueGeneratedNever();
            n.Property(x => x.Text).IsRequired();
            n.Property(x => x.CreatedAt);
            n.Property(x => x.UpdatedAt);
        });
        builder.Navigation(l => l.Notes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
