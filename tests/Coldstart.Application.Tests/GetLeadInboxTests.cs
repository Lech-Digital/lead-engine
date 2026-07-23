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
    public async Task ForCustomerAsync_WhenCustomerExists_ReturnsInboxWithPlanAndLockedCount()
    {
        // Arrange — a Free customer, one locked + one unlocked summary from ListLeads
        var customer = Customer.Create("Coldstart");
        customer.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var open = new LeadSummary(Guid.NewGuid(), "A", "Yes", "PendingReview", null, "None", "s", "b", "hn", 0.9, DateTimeOffset.UtcNow, "x", Locked: false);
        var shut = new LeadSummary(Guid.NewGuid(), "B", "Yes", "PendingReview", null, "None", "s", "b", "hn", 0.8, DateTimeOffset.UtcNow, "x", Locked: true);
        var listLeads = Substitute.For<IListLeads>();
        listLeads.ForCustomerAsync(customer.Id, CustomerPlan.Free, 3, Arg.Any<CancellationToken>()).Returns(new[] { open, shut });
        var sut = new GetLeadInbox(customers, listLeads, new QuotaOptions { FreeLeadCount = 3 });

        // Act
        var result = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        result.CustomerName.Should().Be("Coldstart");
        result.Plan.Should().Be(CustomerPlan.Free);
        result.HarvestStatus.Should().Be(HarvestStatus.Done);
        result.LockedCount.Should().Be(1);
        result.Leads.Should().HaveCount(2);
    }

    [Fact]
    public async Task ForCustomerAsync_WhenCustomerUnknown_ReturnsEmptyInboxWithoutCallingListLeads()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var listLeads = Substitute.For<IListLeads>();
        var sut = new GetLeadInbox(customers, listLeads, new QuotaOptions());

        // Act
        var result = await sut.ForCustomerAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.CustomerName.Should().Be("—");
        result.Leads.Should().BeEmpty();
        result.Plan.Should().Be(CustomerPlan.Free);
        result.HarvestStatus.Should().Be(HarvestStatus.None);
        await listLeads.DidNotReceive().ForCustomerAsync(Arg.Any<CustomerId>(), Arg.Any<CustomerPlan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
