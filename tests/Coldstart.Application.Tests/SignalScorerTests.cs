using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class SignalScorerTests
{
    private static NicheOptions Niche() => new()
    {
        TargetKeywords = ["SDR", "outbound", "pipeline"],
        BuyingSignals = ["hiring", "raised", "series a"],
    };

    private static RawItem Item(string text) => new("t", "1", "", text, "", "", "");

    [Fact]
    public void Score_NoMatches_IsZero()
    {
        // Arrange
        var niche = Niche();

        // Act
        var r = SignalScorer.Score(Item("nothing relevant here"), niche, baseScore: 0, scoreWeight: 1);

        // Assert
        r.Score.Should().Be(0);
        r.Matched.Should().BeEmpty();
    }

    [Fact]
    public void Score_OneTargetOneSignal_IsPointSix()
    {
        // Arrange — 1 target hit (0.6) + 1 signal hit (0.6): 0.5*0.6 + 0.5*0.6 = 0.6
        // (text deliberately avoids also matching "SDR", which would add a 2nd target hit
        //  and push the score to 0.7 — verified against leadengine/signals.py::score_item)
        var niche = Niche();

        // Act
        var r = SignalScorer.Score(Item("we are hiring for outbound sales"), niche, baseScore: 0, scoreWeight: 1);

        // Assert
        r.Score.Should().BeApproximately(0.6, 0.001);
        r.Matched.Should().Contain("outbound").And.Contain("hiring");
    }

    [Fact]
    public void Score_BaseScoreFloor_RaisesButNeverLowers()
    {
        // Arrange — no keyword hits, but a funding-style base floor
        var niche = Niche();

        // Act
        var r = SignalScorer.Score(Item("Acme announced a new round"), niche, baseScore: 0.5, scoreWeight: 1);

        // Assert
        r.Score.Should().Be(0.5);
    }

    [Fact]
    public void Score_Weight_DemotesKeywordScore()
    {
        // Arrange
        var niche = Niche();

        // Act
        var full = SignalScorer.Score(Item("hiring outbound SDR pipeline"), niche, 0, 1.0);
        var demoted = SignalScorer.Score(Item("hiring outbound SDR pipeline"), niche, 0, 0.7);

        // Assert
        demoted.Score.Should().BeLessThan(full.Score);
    }
}
