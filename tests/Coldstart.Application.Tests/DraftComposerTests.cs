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
    public void Compose_WithCompanyTokens_SubstitutesPlainAndUrlEncoded()
    {
        // Arrange — {company} is the display name; {company-url} is URL-safe for query strings.
        var sender = new SenderContext(null, false,
            PitchLine: "An idea for {company}.",
            CtaLine: "Grab 3 free leads: https://getcoldstart.nl/?company={company-url}#get-leads");

        // Act
        var draft = DraftComposer.Compose("Hook.", sender, company: "Acme & Zn B.V.");

        // Assert
        draft.Should().Contain("An idea for Acme & Zn B.V.");
        draft.Should().Contain("?company=Acme%20%26%20Zn%20B.V.#get-leads");
    }

    [Fact]
    public void Compose_WithCtaButtonUrl_AppendsResolvedUrlAsOwnFinalLine()
    {
        // Arrange — the URL gets its own paragraph so plain-text handoffs carry it and
        // the HTML mailer can swap it for a button.
        var sender = new SenderContext(null, false,
            PitchLine: "Pitch.", CtaLine: "Want free leads?",
            CtaButtonUrl: "https://getcoldstart.nl/?company={company-url}#get-leads");

        // Act
        var draft = DraftComposer.Compose("Hook.", sender, company: "Acme B.V.");

        // Assert
        draft.Should().EndWith("Want free leads?\n\nhttps://getcoldstart.nl/?company=Acme%20B.V.#get-leads");
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
