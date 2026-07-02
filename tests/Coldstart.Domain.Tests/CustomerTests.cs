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
}
