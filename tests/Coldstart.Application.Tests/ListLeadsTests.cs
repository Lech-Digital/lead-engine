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
        var result = await sut.ForCustomerAsync(customerId, CustomerPlan.Free, 3, CancellationToken.None);

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
    public async Task ForCustomerAsync_ReturnsAllStatuses_IncludingRejectedAndShelved()
    {
        // Arrange
        var customerId = CustomerId.New();
        Lead Harvested(string company, string fp) => Lead.CreateHarvested(customerId, company, Fit.Yes,
            "subj", "body", "funding", "https://source.example/x", fp, 0.7, "sig", DateTimeOffset.UtcNow);
        var pending = Harvested("Pending Co", "fp-p");
        var rejected = Harvested("Rejected Co", "fp-r");
        rejected.Reject();
        var shelved = Harvested("Shelved Co", "fp-s");
        shelved.Discard();
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new[] { pending, rejected, shelved });
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(customerId, CustomerPlan.Full, 3, CancellationToken.None);

        // Assert — no filtering; the page routes each status to its tab
        result.Should().HaveCount(3);
        result.Select(r => r.Status).Should().Contain(new[] { "PendingReview", "Rejected", "Shelved" });
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
        var result = await sut.ForCustomerAsync(customerId, CustomerPlan.Free, 3, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ForCustomerAsync_FreePlan_TagsLeadsBeyondQuotaAsLocked()
    {
        // Arrange — 2 leads, quota 1
        var cid = new CustomerId(Guid.NewGuid());
        var l1 = Lead.CreateHarvested(cid, "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var l2 = Lead.CreateHarvested(cid, "B", Fit.Yes, "s", "b", "hn", "https://x/2", "fp2", 0.8, "x", DateTimeOffset.UtcNow);
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(cid, Arg.Any<CancellationToken>()).Returns(new[] { l1, l2 });
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(cid, CustomerPlan.Free, 1, CancellationToken.None);

        // Assert
        result.Single(s => s.Id == l1.Id.Value).Locked.Should().BeFalse();
        result.Single(s => s.Id == l2.Id.Value).Locked.Should().BeTrue();
    }
}
