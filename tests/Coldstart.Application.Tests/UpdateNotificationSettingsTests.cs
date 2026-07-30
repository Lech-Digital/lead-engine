using Coldstart.Application.Notifications;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class UpdateNotificationSettingsTests
{
    private static (UpdateNotificationSettings Sut, Customer Customer, ICustomerRepository Repo) Make()
    {
        var customer = Customer.Create("Acme");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        return (new UpdateNotificationSettings(repo), customer, repo);
    }

    [Fact]
    public async Task UpdateAsync_WithValidValues_PersistsBoth()
    {
        // Arrange
        var (sut, customer, repo) = Make();

        // Act
        var error = await sut.UpdateAsync(customer.Id.Value,
            "https://hooks.slack.com/services/T00/B00/xyz", "+31 6 1234 5678", CancellationToken.None);

        // Assert — number is normalized (spaces stripped)
        error.Should().BeNull();
        customer.SlackWebhookUrl.Should().Be("https://hooks.slack.com/services/T00/B00/xyz");
        customer.WhatsAppNumber.Should().Be("+31612345678");
        await repo.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_WithNonSlackUrl_IsRejected()
    {
        // Arrange
        var (sut, customer, repo) = Make();

        // Act
        var error = await sut.UpdateAsync(customer.Id.Value, "https://evil.example/hook", null, CancellationToken.None);

        // Assert
        error.Should().Contain("hooks.slack.com");
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_WithBadPhoneNumber_IsRejected()
    {
        // Arrange
        var (sut, customer, _) = Make();

        // Act
        var error = await sut.UpdateAsync(customer.Id.Value, null, "0612345678", CancellationToken.None);

        // Assert — must be international format
        error.Should().Contain("international");
    }

    [Fact]
    public async Task UpdateAsync_WithEmptyStrings_ClearsBothChannels()
    {
        // Arrange
        var (sut, customer, _) = Make();
        customer.ConfigureNotifications("https://hooks.slack.com/x", "+31612345678");

        // Act
        var error = await sut.UpdateAsync(customer.Id.Value, "", "  ", CancellationToken.None);

        // Assert
        error.Should().BeNull();
        customer.SlackWebhookUrl.Should().BeNull();
        customer.WhatsAppNumber.Should().BeNull();
    }
}
