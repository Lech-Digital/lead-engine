using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class InquiryTests
{
    [Fact]
    public void Create_WithValidFields_SetsProperties()
    {
        // Arrange
        var when = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);

        // Act
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "B2B SaaS", "scheduling", null, when);

        // Assert
        inquiry.Name.Should().Be("Sam");
        inquiry.Email.Value.Should().Be("sam@acme.com");
        inquiry.WhatYouSell.Should().Be("scheduling");
        inquiry.CreatedAt.Should().Be(when);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ThrowsDomainException(string bad)
    {
        // Arrange
        var act = () => Inquiry.Create(bad, new EmailAddress("sam@acme.com"), "n", "s", null, DateTimeOffset.UtcNow);

        // Act & Assert
        act.Should().Throw<DomainException>();
    }
}
