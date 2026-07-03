using Coldstart.Application.Contact;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class SubmitInquiryTests
{
    private static SubmitInquiryRequest Req() => new("Sam", "sam@acme.com", "B2B SaaS", "scheduling", "hi");

    [Fact]
    public async Task SubmitAsync_StoresInquiry_AndSendsNotification()
    {
        // Arrange
        var repo = Substitute.For<IInquiryRepository>();
        var email = Substitute.For<IEmailSender>();
        var sut = new SubmitInquiry(repo, email);

        // Act
        await sut.SubmitAsync(Req(), CancellationToken.None);

        // Assert
        await repo.Received(1).AddAsync(Arg.Is<Inquiry>(i => i.Name == "Sam" && i.Email.Value == "sam@acme.com"), Arg.Any<CancellationToken>());
        await email.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string>(b => b.Contains("Sam")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenEmailSenderThrows_StillSucceeds()
    {
        // Arrange
        var repo = Substitute.For<IInquiryRepository>();
        var email = Substitute.For<IEmailSender>();
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));
        var sut = new SubmitInquiry(repo, email);

        // Act
        var act = async () => await sut.SubmitAsync(Req(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        await repo.Received(1).AddAsync(Arg.Any<Inquiry>(), Arg.Any<CancellationToken>());
    }
}
