using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
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
        builder.Property(l => l.RecipientEmail)
            .HasConversion(
                email => email!.Value,
                value => new EmailAddress(value));
        builder.Property(l => l.CreatedAt);
        builder.Property(l => l.SentAt);
    }
}
