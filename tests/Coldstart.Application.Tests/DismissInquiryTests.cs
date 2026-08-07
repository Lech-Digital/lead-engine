using Coldstart.Application.Contact;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class DismissInquiryTests
{
    private static Inquiry NewInquiry() =>
        Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "B2B SaaS", "scheduling", "hi", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ExecuteAsync_WhenInquiryExists_DismissesItAndPersists()
    {
        // Arrange
        var inquiry = NewInquiry();
        var repo = Substitute.For<IInquiryRepository>();
        repo.GetAsync(inquiry.Id, Arg.Any<CancellationToken>()).Returns(inquiry);
        var sut = new DismissInquiry(repo);
        var when = DateTimeOffset.UtcNow;

        // Act
        var result = await sut.ExecuteAsync(inquiry.Id.Value, when, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        inquiry.DismissedAt.Should().Be(when);
        await repo.Received(1).UpdateAsync(inquiry, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoSuchInquiry_FailsWithoutTouchingTheRepository()
    {
        // Arrange
        var repo = Substitute.For<IInquiryRepository>();
        repo.GetAsync(Arg.Any<InquiryId>(), Arg.Any<CancellationToken>()).Returns((Inquiry?)null);
        var sut = new DismissInquiry(repo);

        // Act
        var result = await sut.ExecuteAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Inquiry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyDismissed_SucceedsAndRefreshesTheTimestamp()
    {
        // Arrange
        var inquiry = NewInquiry();
        inquiry.Dismiss(DateTimeOffset.UtcNow.AddDays(-3));
        var repo = Substitute.For<IInquiryRepository>();
        repo.GetAsync(inquiry.Id, Arg.Any<CancellationToken>()).Returns(inquiry);
        var sut = new DismissInquiry(repo);
        var when = DateTimeOffset.UtcNow;

        // Act
        var result = await sut.ExecuteAsync(inquiry.Id.Value, when, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        inquiry.DismissedAt.Should().Be(when);
    }
}
