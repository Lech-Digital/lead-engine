using Coldstart.Application.Contact;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class RestoreInquiryTests
{
    private static Inquiry DismissedInquiry()
    {
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "B2B SaaS", "scheduling", "hi", DateTimeOffset.UtcNow);
        inquiry.Dismiss(DateTimeOffset.UtcNow);
        return inquiry;
    }

    [Fact]
    public async Task ExecuteAsync_WhenInquiryIsDismissed_RestoresItAndPersists()
    {
        // Arrange
        var inquiry = DismissedInquiry();
        var repo = Substitute.For<IInquiryRepository>();
        repo.GetAsync(inquiry.Id, Arg.Any<CancellationToken>()).Returns(inquiry);
        var sut = new RestoreInquiry(repo);

        // Act
        var result = await sut.ExecuteAsync(inquiry.Id.Value, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        inquiry.DismissedAt.Should().BeNull();
        await repo.Received(1).UpdateAsync(inquiry, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoSuchInquiry_FailsWithoutTouchingTheRepository()
    {
        // Arrange
        var repo = Substitute.For<IInquiryRepository>();
        repo.GetAsync(Arg.Any<InquiryId>(), Arg.Any<CancellationToken>()).Returns((Inquiry?)null);
        var sut = new RestoreInquiry(repo);

        // Act
        var result = await sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Inquiry>(), Arg.Any<CancellationToken>());
    }
}
