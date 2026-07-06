using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class ListLeadsTests
{
    [Fact]
    public async Task ForCustomerAsync_MapsDomainLeadsToSummaries()
    {
        // Arrange
        var customerId = CustomerId.New();
        var lead = Lead.CreateHarvested(customerId, "Patronus AI", Fit.Yes,
            "A thought after the $50M", "Body", "funding", "https://source.example/funding",
            "fingerprint-1", 0.7, "matched signals", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress("anand@patronus.ai"));
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new[] { lead });
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(customerId, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Company.Should().Be("Patronus AI");
        result[0].Status.Should().Be("Approved");
        result[0].RecipientEmail.Should().Be("anand@patronus.ai");
        result[0].Subject.Should().Be("A thought after the $50M");
        result[0].Draft.Should().Be("Body");
        result[0].Source.Should().Be("funding");
        result[0].Score.Should().Be(0.7);
    }

    [Fact]
    public async Task ForCustomerAsync_WhenNoLeads_ReturnsEmpty()
    {
        // Arrange
        var customerId = CustomerId.New();
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Lead>());
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(customerId, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
