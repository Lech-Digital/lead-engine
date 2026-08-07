using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class CandidatePriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rank_ItemWithMatchedSignals_OutranksOneWithout()
    {
        // Arrange
        var matched = new SignalResult(0.8, ["raised seed"]);
        var unmatched = new SignalResult(0.25, []);

        // Act
        var withSignal = CandidatePriority.Rank(matched, 0.25, Now, Now);
        var without = CandidatePriority.Rank(unmatched, 0.25, Now, Now);

        // Assert
        withSignal.Should().BeGreaterThan(without);
    }

    [Fact]
    public void Rank_LowBaseScoreSource_StillProducesAUsableRank()
    {
        // Arrange — LangSearch's 0.25 base is below the old 0.4 gate, which killed it entirely
        var signal = new SignalResult(0.25, []);

        // Act
        var rank = CandidatePriority.Rank(signal, 0.25, Now, Now);

        // Assert — it must compete, not be discarded
        rank.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rank_FresherItem_OutranksAnOlderOneWithIdenticalSignal()
    {
        // Arrange
        var signal = new SignalResult(0.5, []);

        // Act
        var fresh = CandidatePriority.Rank(signal, 0.5, Now, Now);
        var old = CandidatePriority.Rank(signal, 0.5, Now.AddDays(-10), Now);

        // Assert
        fresh.Should().BeGreaterThan(old);
    }

    [Fact]
    public void Rank_UnknownDate_IsRankedWithoutARecencyBonusRatherThanPenalised()
    {
        // Arrange
        var signal = new SignalResult(0.5, []);

        // Act
        var unknown = CandidatePriority.Rank(signal, 0.5, null, Now);
        var old = CandidatePriority.Rank(signal, 0.5, Now.AddDays(-10), Now);

        // Assert — a missing date must not push a source to the bottom forever
        unknown.Should().BeGreaterThanOrEqualTo(old);
    }
}
