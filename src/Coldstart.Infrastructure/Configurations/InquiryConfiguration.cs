using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class InquiryConfiguration : IEntityTypeConfiguration<Inquiry>
{
    public void Configure(EntityTypeBuilder<Inquiry> builder)
    {
        builder.ToTable("inquiries");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InquiryId(value))
            .ValueGeneratedNever();
        builder.Property(i => i.Name).IsRequired();
        builder.Property(i => i.Email)
            .HasConversion(e => e.Value, v => new EmailAddress(v))
            .IsRequired();
        builder.Property(i => i.Niche);
        builder.Property(i => i.WhatYouSell);
        builder.Property(i => i.Message);
        builder.Property(i => i.CreatedAt);
    }
}
