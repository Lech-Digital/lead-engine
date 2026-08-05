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
        builder.Property(c => c.Plan).HasConversion<string>().HasMaxLength(16).HasDefaultValue(CustomerPlan.Free);
        builder.Property(c => c.HarvestStatus).HasConversion<string>().HasMaxLength(16).HasDefaultValue(HarvestStatus.None);
        builder.Property(c => c.LastHarvestAt);
        builder.Property(c => c.StripeCustomerId).HasMaxLength(64);
        builder.Property(c => c.StripeSubscriptionId).HasMaxLength(64);
        builder.Property(c => c.PlanRenewsAt);
        builder.Property(c => c.ComplimentaryUntil);
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
            p.Property(x => x.Offer).HasColumnName("ProfileOffer");
            p.Property(x => x.TargetKeywords).HasColumnName("ProfileTargetKeywords");
            p.Property(x => x.BuyingSignals).HasColumnName("ProfileBuyingSignals");
            p.Property(x => x.Disqualifiers).HasColumnName("ProfileDisqualifiers");
            p.Property(x => x.PitchLine).HasColumnName("ProfilePitchLine");
            p.Property(x => x.CtaLine).HasColumnName("ProfileCtaLine");
            p.Property(x => x.CtaButtonText).HasColumnName("ProfileCtaButtonText");
            p.Property(x => x.CtaButtonUrl).HasColumnName("ProfileCtaButtonUrl");
            p.Property(x => x.IntroLine).HasColumnName("ProfileIntroLine");
            p.Property(x => x.SubjectTemplate).HasColumnName("ProfileSubjectTemplate");
        });
        builder.Property(c => c.SlackWebhookUrl);
        builder.Property(c => c.EnabledSourcesCsv);
        builder.Property(c => c.FollowUpStage).HasDefaultValue(0);
        builder.Property(c => c.FollowUpsOptOut).HasDefaultValue(false);
        builder.Property(c => c.SubscriptionEndedAt);
        builder.Property(c => c.RetentionReminderSent).HasDefaultValue(false);
        builder.Property(c => c.WhatsAppNumber);
        builder.Navigation(c => c.Profile).IsRequired();   // Profile is never null (defaults to Empty)
    }
}
