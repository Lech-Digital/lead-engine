using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class GetLeadInboxTests
{
    [Fact]
    public async Task ForCustomerAsync_WhenCustomerExists_ReturnsInboxWithLeads()
    {
        // Arrange
        var customer = Customer.Create("Coldstart");
        var customers = Substitute.For<ICustomerRepository>();
        customers.AllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { customer });
        var summary = new LeadSummary(Guid.NewGuid(), "Patronus AI", "Yes", "Approved", "anand@patronus.ai", "High",
            "A thought after the $50M", "Body", "funding", 0.7, DateTimeOffset.UtcNow, "raised $50M Series C");
        var listLeads = Substitute.For<IListLeads>();
        listLeads.ForCustomerAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { summary });
        var sut = new GetLeadInbox(customers, listLeads);

        // Act
        var result = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        result.CustomerName.Should().Be("Coldstart");
        result.Leads.Should().ContainSingle();
        result.Leads[0].Should().Be(summary);
    }

    [Fact]
    public async Task ForCustomerAsync_WhenCustomerUnknown_ReturnsEmptyInboxWithoutCallingListLeads()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        customers.AllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Customer>());
        var listLeads = Substitute.For<IListLeads>();
        var sut = new GetLeadInbox(customers, listLeads);

        // Act
        var result = await sut.ForCustomerAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.CustomerName.Should().Be("—");
        result.Leads.Should().BeEmpty();
        await listLeads.DidNotReceive().ForCustomerAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>());
    }
}
