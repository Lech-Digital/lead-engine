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

        // Assert — every skeleton keeps greeting rule, honesty, banned list, and the
        // no-signature instruction (round-1 review finding: the send pipeline unconditionally
        // appends a real signature, so the model must never write its own).
        variants.Should().HaveCount(5);
        variants.Should().AllSatisfy(v =>
        {
            v.Should().Contain("Hi there,");
            v.Should().Contain("Never guess or invent a name");
            v.Should().Contain("Banned:");
            v.Should().Contain("No signature (one is added on send).");
        });
    }

    [Fact]
    public void NormalDraftInstruction_FiveVariants_HaveDistinctParagraphStructures()
    {
        // Arrange — sample enough keys to hit all 5 variants (mirrors the invariants test above)
        var niche = new NicheOptions { SenderName = "Ann", SenderTitle = "Founder" };
        var variants = Enumerable.Range(0, 200)
            .Select(i => DraftSkeletons.NormalDraftInstruction($"lead-{i}", niche))
            .Distinct().ToList();
        variants.Should().HaveCount(5);

        // Act — count the structural directive lines ("- ...") per variant, a mechanical
        // proxy for paragraph COUNT: the axis a spam filter (and a human skimming a batch)
        // actually notices across sends, as opposed to which noun frames paragraph 1.
        var bulletCounts = variants
            .Select(v => v.Split('\n').Count(l => l.TrimStart().StartsWith("- ")))
            .ToList();

        // Assert — the five shapes are not all the same paragraph count
        bulletCounts.Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public void NormalDraftInstruction_FiveVariants_ExactlyOneUsesTheDashedListShape()
    {
        // Arrange — round-2 rework: shape 5 (the reworked former "Inverted" variant) shares its
        // paragraph-block count (4) with shapes 1 and 3, so bullet-count alone no longer proves
        // it apart from them. Its actual structural signature is the two-line em-dash ("— ")
        // list block — a form none of the other four shapes use — which is what visually and
        // structurally sets it apart. This test locks that in as a permanent regression guard.
        var niche = new NicheOptions { SenderName = "Ann", SenderTitle = "Founder" };
        var variants = Enumerable.Range(0, 200)
            .Select(i => DraftSkeletons.NormalDraftInstruction($"lead-{i}", niche))
            .Distinct().ToList();
        variants.Should().HaveCount(5);

        // Act
        var withDashedList = variants.Count(v => v.Contains("em dash"));

        // Assert — exactly one of the five shapes uses the dashed-list construct
        withDashedList.Should().Be(1);
    }

    [Fact]
    public void FixedPitchDraftInstruction_AllVariants_KeepQuotedAnalysisAndHonesty()
    {
        // Act
        var variants = Enumerable.Range(0, 200)
            .Select(i => DraftSkeletons.FixedPitchDraftInstruction($"lead-{i}"))
            .Distinct().ToList();

        // Assert — round-1 review also asked for the no-signature line defensively here: the
        // AI hook is composed BEFORE the pitch/CTA blocks, so nothing stops the model writing
        // its own sign-off first.
        variants.Should().HaveCount(3);
        variants.Should().AllSatisfy(v =>
        {
            v.Should().Contain("> ");                       // quoted analysis stays
            v.Should().Contain("do NOT sell");              // appended-pitch contract stays
            v.Should().Contain("never claim leads are already found");
            v.Should().Contain("No signature (one is added on send).");
        });
    }
}
