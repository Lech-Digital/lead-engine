using Coldstart.Application.Harvest;
using Coldstart.Application.Notifications;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class NotifyBriefingTests
{
    private sealed class FakeChannel(string name, bool canSend, bool throws = false) : IBriefingChannel
    {
        public string Name => name;
        public List<BriefingNotification> Sent { get; } = [];
        public bool CanSend(Customer customer) => canSend;
        public Task SendAsync(Customer customer, BriefingNotification notification, CancellationToken ct)
        {
            if (throws) throw new InvalidOperationException("channel down");
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private static readonly IReadOnlyList<SavedLead> Leads = [new(Guid.NewGuid(), "Acme", "Subject")];

    private static ICustomerRepository RepoWith(Customer customer)
    {
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        return repo;
    }

    [Fact]
    public async Task NotifyAsync_SendsOnConfiguredChannels_AndSkipsUnconfigured()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var slack = new FakeChannel("slack", canSend: true);
        var whatsApp = new FakeChannel("whatsapp", canSend: false);
        var sut = new NotifyBriefing(RepoWith(customer), [slack, whatsApp]);

        // Act
        var sent = await sut.NotifyAsync(customer.Id.Value, Leads);

        // Assert
        sent.Should().Equal("slack");
        slack.Sent.Should().ContainSingle().Which.Leads.Should().HaveCount(1);
        whatsApp.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyAsync_WhenOneChannelThrows_StillSendsTheOther()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var broken = new FakeChannel("slack", canSend: true, throws: true);
        var healthy = new FakeChannel("whatsapp", canSend: true);
        var sut = new NotifyBriefing(RepoWith(customer), [broken, healthy]);

        // Act
        var sent = await sut.NotifyAsync(customer.Id.Value, Leads);

        // Assert — the failure is contained; the healthy channel still fires
        sent.Should().Equal("whatsapp");
        healthy.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task NotifyAsync_WithChannelFilter_SendsOnlyThatChannel()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var slack = new FakeChannel("slack", canSend: true);
        var whatsApp = new FakeChannel("whatsapp", canSend: true);
        var sut = new NotifyBriefing(RepoWith(customer), [slack, whatsApp]);

        // Act
        var sent = await sut.NotifyAsync(customer.Id.Value, Leads, channelFilter: "whatsapp");

        // Assert
        sent.Should().Equal("whatsapp");
        slack.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyAsync_WithNoLeads_SendsNothing()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var slack = new FakeChannel("slack", canSend: true);
        var sut = new NotifyBriefing(RepoWith(customer), [slack]);

        // Act
        var sent = await sut.NotifyAsync(customer.Id.Value, []);

        // Assert
        sent.Should().BeEmpty();
        slack.Sent.Should().BeEmpty();
    }
}
