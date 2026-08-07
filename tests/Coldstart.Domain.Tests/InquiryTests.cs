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

    [Fact]
    public void Create_ByDefault_IsNotDismissed()
    {
        // Arrange & Act
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "n", "s", null, DateTimeOffset.UtcNow);

        // Assert
        inquiry.DismissedAt.Should().BeNull();
    }

    [Fact]
    public void Dismiss_WhenOpen_SetsDismissedAt()
    {
        // Arrange
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "n", "s", null, DateTimeOffset.UtcNow);
        var when = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

        // Act
        inquiry.Dismiss(when);

        // Assert
        inquiry.DismissedAt.Should().Be(when);
    }

    [Fact]
    public void Dismiss_WhenAlreadyDismissed_IsIdempotentAndUpdatesTheTimestamp()
    {
        // Arrange — dismissed once already
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "n", "s", null, DateTimeOffset.UtcNow);
        var first = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        inquiry.Dismiss(first);

        // Act — dismiss again (e.g. a double-submit or a second admin tab)
        var act = () => inquiry.Dismiss(second);

        // Assert — no exception, and the timestamp reflects the latest call
        act.Should().NotThrow();
        inquiry.DismissedAt.Should().Be(second);
    }

    [Fact]
    public void Restore_WhenDismissed_ClearsDismissedAt()
    {
        // Arrange
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "n", "s", null, DateTimeOffset.UtcNow);
        inquiry.Dismiss(DateTimeOffset.UtcNow);

        // Act
        inquiry.Restore();

        // Assert
        inquiry.DismissedAt.Should().BeNull();
    }

    [Fact]
    public void Restore_WhenNotDismissed_IsIdempotentNoOp()
    {
        // Arrange
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "n", "s", null, DateTimeOffset.UtcNow);

        // Act
        var act = () => inquiry.Restore();

        // Assert
        act.Should().NotThrow();
        inquiry.DismissedAt.Should().BeNull();
    }

    [Fact]
    public void DismissThenRestore_NeverDeletesTheInquiry_AllOtherFieldsSurvive()
    {
        // Arrange
        var when = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "B2B SaaS", "scheduling", "hi", when, "Acme", when);

        // Act
        inquiry.Dismiss(DateTimeOffset.UtcNow);
        inquiry.Restore();

        // Assert — the record itself, and every other field, is intact — only DismissedAt moved
        inquiry.Name.Should().Be("Sam");
        inquiry.Email.Value.Should().Be("sam@acme.com");
        inquiry.Company.Should().Be("Acme");
        inquiry.ConsentGivenAt.Should().Be(when);
        inquiry.DismissedAt.Should().BeNull();
    }
}
