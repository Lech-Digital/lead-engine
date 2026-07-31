using Coldstart.Application.FollowUps;
using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class SendTrialFollowUpsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private static LeadSummary Lead(bool locked) => new(
        Guid.NewGuid(), "Acme", "Good", "PendingReview", null, "None",
        "Subject", "Draft", "hackernews", 0.8, Now.AddDays(-3), "", locked);

    private sealed class FakeSender(bool result = true) : ITrialFollowUpSender
    {
        public List<TrialFollowUp> Sent { get; } = [];
        public Task<bool> SendAsync(TrialFollowUp followUp, CancellationToken ct)
        {
            Sent.Add(followUp);
            return Task.FromResult(result);
        }
    }

    private static (SendTrialFollowUps Sut, FakeSender Sender, ICustomerRepository Repo, Customer Customer) Build(
        int daysSinceHarvest, int stageAlreadySent, bool senderResult = true, int lockedLeads = 2)
    {
        var customer = Customer.Create("Acme");
        customer.MarkHarvest(HarvestStatus.Done, Now.AddDays(-daysSinceHarvest));
        customer.MarkFollowUpSent(stageAlreadySent);

        var repo = Substitute.For<ICustomerRepository>();
        repo.TrialFollowUpCandidatesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new TrialFollowUpCandidate(customer.Id.Value, stageAlreadySent, Now.AddDays(-daysSinceHarvest))]);
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);

        var listLeads = Substitute.For<IListLeads>();
        var leads = Enumerable.Range(0, 3).Select(_ => Lead(locked: false))
            .Concat(Enumerable.Range(0, lockedLeads).Select(_ => Lead(locked: true))).ToList();
        listLeads.ForCustomerAsync(Arg.Any<CustomerId>(), Arg.Any<CustomerPlan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LeadSummary>)leads);

        var sender = new FakeSender(senderResult);
        var sut = new SendTrialFollowUps(repo, Substitute.For<ITenantScope>(), listLeads, new QuotaOptions(), sender);
        return (sut, sender, repo, customer);
    }

    [Fact]
    public async Task RunAsync_TwoDaysAfterHarvest_SendsDay2WithLockedCount_AndMarksStage()
    {
        // Arrange
        var (sut, sender, repo, customer) = Build(daysSinceHarvest: 2, stageAlreadySent: 0);

        // Act
        var sent = await sut.RunAsync(Now, CancellationToken.None);

        // Assert
        sent.Should().Be(1);
        sender.Sent.Should().ContainSingle();
        sender.Sent[0].Stage.Should().Be(TrialFollowUpStage.Day2);
        sender.Sent[0].LockedLeadCount.Should().Be(2);
        customer.FollowUpStage.Should().Be(TrialFollowUpStage.Day2);
        await repo.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FiveDaysAfterHarvestNothingSentYet_SendsOnlyDay5()
    {
        // Arrange — discovered late: skip straight to the final nudge, never two at once
        var (sut, sender, _, customer) = Build(daysSinceHarvest: 6, stageAlreadySent: 0);

        // Act
        await sut.RunAsync(Now, CancellationToken.None);

        // Assert
        sender.Sent.Should().ContainSingle().Which.Stage.Should().Be(TrialFollowUpStage.Day5);
        customer.FollowUpStage.Should().Be(TrialFollowUpStage.Day5);
    }

    [Fact]
    public async Task RunAsync_Day2AlreadySentAndNotYetDay5_SendsNothing()
    {
        // Arrange
        var (sut, sender, repo, _) = Build(daysSinceHarvest: 3, stageAlreadySent: TrialFollowUpStage.Day2);

        // Act
        var sent = await sut.RunAsync(Now, CancellationToken.None);

        // Assert
        sent.Should().Be(0);
        sender.Sent.Should().BeEmpty();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CustomerSubscribedSinceQuery_SkipsWithoutSending()
    {
        // Arrange — the candidate query saw Free, but the customer paid before the send
        var (sut, sender, _, customer) = Build(daysSinceHarvest: 2, stageAlreadySent: 0);
        customer.ActivateFull("sub_123", Now.AddMonths(1));

        // Act
        var sent = await sut.RunAsync(Now, CancellationToken.None);

        // Assert
        sent.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenSenderReportsNoAddress_LeavesStageUnmarkedForRetry()
    {
        // Arrange
        var (sut, sender, repo, customer) = Build(daysSinceHarvest: 2, stageAlreadySent: 0, senderResult: false);

        // Act
        var sent = await sut.RunAsync(Now, CancellationToken.None);

        // Assert
        sent.Should().Be(0);
        sender.Sent.Should().ContainSingle();   // attempted…
        customer.FollowUpStage.Should().Be(TrialFollowUpStage.None);   // …but not marked
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, 0, TrialFollowUpStage.None)]    // too fresh
    [InlineData(2, 0, TrialFollowUpStage.Day2)]
    [InlineData(4, 0, TrialFollowUpStage.Day2)]
    [InlineData(5, 0, TrialFollowUpStage.Day5)]    // late discovery skips day-2
    [InlineData(5, 1, TrialFollowUpStage.Day5)]
    [InlineData(5, 2, TrialFollowUpStage.None)]    // fully nudged
    [InlineData(3, 1, TrialFollowUpStage.None)]    // day-2 sent, day-5 not yet due
    public void DueStage_CoversTheCalendar(int daysAgo, int alreadySent, int expected)
    {
        // Arrange
        var leadsAt = Now.AddDays(-daysAgo);

        // Act
        var stage = SendTrialFollowUps.DueStage(leadsAt, alreadySent, Now);

        // Assert
        stage.Should().Be(expected);
    }
}
