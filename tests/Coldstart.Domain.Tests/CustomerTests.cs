using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class CustomerTests
{
    [Fact]
    public void Create_SetsNameAndEmptyLeads()
    {
        // Act
        var customer = Customer.Create("Coldstart");

        // Assert
        customer.Name.Should().Be("Coldstart");
        customer.Leads.Should().BeEmpty();
    }

    [Fact]
    public void AddLead_AttachesLeadToCustomer()
    {
        // Arrange
        var customer = Customer.Create("Coldstart");

        // Act
        var lead = customer.AddLead("Patronus AI", Fit.Yes, "Subj", "Body", DateTimeOffset.UtcNow);

        // Assert
        customer.Leads.Should().ContainSingle().Which.Should().BeSameAs(lead);
        lead.CustomerId.Should().Be(customer.Id);
    }

    [Fact]
    public void Create_DefaultsToFreePlanAndNoHarvest()
    {
        // Act
        var customer = Customer.Create("Acme");

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Free);
        customer.HarvestStatus.Should().Be(HarvestStatus.None);
        customer.LastHarvestAt.Should().BeNull();
    }

    [Fact]
    public void SetPlan_ChangesPlan()
    {
        // Arrange
        var customer = Customer.Create("Acme");

        // Act
        customer.SetPlan(CustomerPlan.Full);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Full);
    }

    [Fact]
    public void MarkHarvest_SetsStatusAndStampsTimeWhenGiven()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var at = DateTimeOffset.UtcNow;

        // Act
        customer.MarkHarvest(HarvestStatus.Running, null);   // no timestamp yet
        customer.MarkHarvest(HarvestStatus.Done, at);

        // Assert
        customer.HarvestStatus.Should().Be(HarvestStatus.Done);
        customer.LastHarvestAt.Should().Be(at);
    }
}
