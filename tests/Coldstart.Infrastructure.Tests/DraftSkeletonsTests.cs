using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class DraftSkeletonsTests
{
    [Fact]
    public void IndexFor_SameKey_IsDeterministic()
    {
        // Arrange / Act / Assert — stable across calls (and across processes, by construction)
        DraftSkeletons.IndexFor("lead-abc", 5).Should().Be(DraftSkeletons.IndexFor("lead-abc", 5));
    }

    [Fact]
    public void IndexFor_ManyKeys_CoversAllVariants()
    {
        // Arrange
        var keys = Enumerable.Range(0, 200).Select(i => $"lead-{i}");

        // Act
        var hit = keys.Select(k => DraftSkeletons.IndexFor(k, 5)).Distinct().OrderBy(x => x).ToList();

        // Assert — 200 leads must spread over all 5 skeletons
        hit.Should().BeEquivalentTo([0, 1, 2, 3, 4]);
    }

    [Fact]
    public void IndexFor_EmptyKey_StillReturnsValidIndex()
    {
        // Arrange / Act
        var index = DraftSkeletons.IndexFor("", 5);

        // Assert
        index.Should().BeInRange(0, 4);
    }

    [Fact]
    public void SubjectInstruction_DifferentKeys_ProduceDifferentPatterns()
    {
        // Arrange
        var keys = Enumerable.Range(0, 50).Select(i => $"lead-{i}");

        // Act
        var patterns = keys.Select(DraftSkeletons.SubjectInstruction).Distinct().Count();

        // Assert
        patterns.Should().BeGreaterThan(1);
    }

    [Fact]
    public void NormalDraftInstruction_AllVariants_KeepSharedInvariants()
    {
        // Arrange
        var niche = new NicheOptions { SenderName = "Ann", SenderTitle = "Founder" };

        // Act — sample enough keys to hit all 5 variants
        var variants = Enumerable.Range(0, 200)
            .Select(i => DraftSkeletons.NormalDraftInstruction($"lead-{i}", niche))
            .Distinct().ToList();

        // Assert — every skeleton keeps greeting rule, honesty and banned list
        variants.Should().HaveCount(5);
        variants.Should().AllSatisfy(v =>
        {
            v.Should().Contain("Hi there,");
            v.Should().Contain("Never guess or invent a name");
            v.Should().Contain("Banned:");
        });
    }

    [Fact]
    public void FixedPitchDraftInstruction_AllVariants_KeepQuotedAnalysisAndHonesty()
    {
        // Act
        var variants = Enumerable.Range(0, 200)
            .Select(i => DraftSkeletons.FixedPitchDraftInstruction($"lead-{i}"))
            .Distinct().ToList();

        // Assert
        variants.Should().HaveCount(3);
        variants.Should().AllSatisfy(v =>
        {
            v.Should().Contain("> ");                       // quoted analysis stays
            v.Should().Contain("do NOT sell");              // appended-pitch contract stays
            v.Should().Contain("never claim leads are already found");
        });
    }
}
