using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .ValueGeneratedNever();
        builder.Property(c => c.Name).IsRequired();
        builder.HasMany(c => c.Leads)
            .WithOne()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Customer.Leads))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsOne(c => c.Profile, p =>
        {
            p.Property(x => x.CompanyName).HasColumnName("ProfileCompanyName");
            p.Property(x => x.Address).HasColumnName("ProfileAddress");
            p.Property(x => x.VatNumber).HasColumnName("ProfileVatNumber");
            p.Property(x => x.KvkNumber).HasColumnName("ProfileKvkNumber");
            p.Property(x => x.Niche).HasColumnName("ProfileNiche");
            p.Property(x => x.Website).HasColumnName("ProfileWebsite");
            p.Property(x => x.ContactEmail).HasColumnName("ProfileContactEmail");
            p.Property(x => x.Brief).HasColumnName("ProfileBrief");
            p.Property(x => x.MentionSourceTool).HasColumnName("ProfileMentionSourceTool").HasDefaultValue(false);
        });
        builder.Navigation(c => c.Profile).IsRequired();   // Profile is never null (defaults to Empty)
    }
}
