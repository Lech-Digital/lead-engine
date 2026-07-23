using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class CustomerProfileTests
{
    [Fact]
    public void NewCustomer_HasEmptyProfile()
    {
        // Act
        var customer = Customer.Create("sam@acme.com");

        // Assert
        customer.Profile.Should().Be(CompanyProfile.Empty);
    }

    [Fact]
    public void UpdateProfile_ReplacesTheProfile()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var profile = new CompanyProfile("Acme Ltd", "1 Main St", "NL123", "KVK9", "B2B SaaS", "acme.com", "hi@acme.com");

        // Act
        customer.UpdateProfile(profile);

        // Assert
        customer.Profile.Should().Be(profile);
        customer.Profile.CompanyName.Should().Be("Acme Ltd");
    }

    [Fact]
    public void UpdateProfile_WithNull_ResetsToEmpty()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        customer.UpdateProfile(new CompanyProfile("Acme", null, null, null, null, null, null));

        // Act
        customer.UpdateProfile(null!);

        // Assert
        customer.Profile.Should().Be(CompanyProfile.Empty);
    }

    [Fact]
    public void Empty_HasNoBriefAndDoesNotMentionTheSourceTool()
    {
        // Act
        var profile = CompanyProfile.Empty;

        // Assert
        profile.Brief.Should().BeNull();
        profile.MentionSourceTool.Should().BeFalse();
    }
}
