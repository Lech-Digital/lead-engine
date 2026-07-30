using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class DraftComposerTests
{
    [Fact]
    public void Compose_WithPitchAndCta_AppendsBlocksAsParagraphs()
    {
        // Arrange
        var sender = new SenderContext(null, false,
            PitchLine: "I run Coldstart — it finds warm leads overnight.",
            CtaLine: "Want 3 free leads? https://getcoldstart.nl/#get-leads");
        var hook = "Hi there,\n\nYour $50M round means the sales team needs pipeline now.";

        // Act
        var draft = DraftComposer.Compose(hook, sender);

        // Assert — hook, pitch, and CTA are separate paragraphs in that order
        draft.Should().Be(
            "Hi there,\n\nYour $50M round means the sales team needs pipeline now.\n\n" +
            "I run Coldstart — it finds warm leads overnight.\n\n" +
            "Want 3 free leads? https://getcoldstart.nl/#get-leads");
    }

    [Fact]
    public void Compose_WithoutFixedBlocks_ReturnsAiDraftUnchanged()
    {
        // Arrange
        var sender = new SenderContext("some brief", true);

        // Act
        var draft = DraftComposer.Compose("Hi there,\n\nFull AI draft.", sender);

        // Assert
        draft.Should().Be("Hi there,\n\nFull AI draft.");
    }

    [Fact]
    public void Compose_WithOnlyCta_SkipsTheBlankPitchBlock()
    {
        // Arrange
        var sender = new SenderContext(null, false, PitchLine: "  ", CtaLine: "Try it free.");

        // Act
        var draft = DraftComposer.Compose("Hook.", sender);

        // Assert — no empty paragraph where the pitch would have been
        draft.Should().Be("Hook.\n\nTry it free.");
    }

    [Fact]
    public void HasFixedPitch_TracksPitchAndCtaPresence()
    {
        // Arrange / Act / Assert
        new SenderContext(null, false).HasFixedPitch.Should().BeFalse();
        new SenderContext(null, false, PitchLine: "p").HasFixedPitch.Should().BeTrue();
        new SenderContext(null, false, CtaLine: "c").HasFixedPitch.Should().BeTrue();
    }
}
