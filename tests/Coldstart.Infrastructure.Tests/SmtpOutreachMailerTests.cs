using Coldstart.Application.Outreach;
using Coldstart.Infrastructure.Outreach;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class SmtpOutreachMailerTests
{
    private static OutreachMailboxOptions Opts() => new()
    {
        Host = "mail.example.test", Username = "andrzej@getcoldstart.nl",
        Password = "x", FromName = "Andrzej — Coldstart",
    };

    [Fact]
    public void BuildMessage_SetsEnvelope_FromConfiguredIdentity()
    {
        // Arrange
        var mail = new OutreachEmail("ceo@acme.com", "About your $15M raise", "First line.\n\nSecond line.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(Opts(), mail);

        // Assert
        msg.Subject.Should().Be("About your $15M raise");
        msg.To.ToString().Should().Contain("ceo@acme.com");
        msg.From.ToString().Should().Contain("andrzej@getcoldstart.nl");
    }

    [Fact]
    public void BuildMessage_WithRelaySmtpAccount_UsesFromAddressNotUsername()
    {
        // Arrange — on a relay like Resend the SMTP username is a login token ("resend"),
        // not a mailbox; the From address must come from its own setting.
        var opts = Opts();
        opts.Username = "resend";
        opts.FromAddress = "andrzej@getcoldstart.nl";
        var mail = new OutreachEmail("ceo@acme.com", "Subject", "Body.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(opts, mail);

        // Assert
        msg.From.ToString().Should().Contain("andrzej@getcoldstart.nl");
        msg.From.ToString().Should().NotContain("resend");
    }

    [Fact]
    public void BuildMessage_BrandsHtml_WithCssSignature_AndNoImages()
    {
        // Arrange
        var mail = new OutreachEmail("ceo@acme.com", "s", "Hello there.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(Opts(), mail);

        // Assert — deliverability-safe: branded via text/CSS, never an <img>
        msg.HtmlBody.Should().NotBeNull();
        msg.HtmlBody.Should().Contain("coldstart");            // wordmark in the signature
        msg.HtmlBody.Should().Contain("Founder, Coldstart");
        msg.HtmlBody.Should().NotContain("<img");
        msg.HtmlBody.Should().Contain("Hello there.");         // the body is present
    }

    [Fact]
    public void BuildMessage_IncludesPlainTextAlternative_WithBodyAndPlainSignature()
    {
        // Arrange
        var mail = new OutreachEmail("ceo@acme.com", "s", "Body copy here.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(Opts(), mail);

        // Assert
        msg.TextBody.Should().NotBeNull();
        msg.TextBody.Should().Contain("Body copy here.");
        msg.TextBody.Should().Contain("Founder, Coldstart");
        msg.TextBody.Should().NotContain("<");                 // plain part carries no markup
    }

    [Fact]
    public void BuildMessage_WhenBodyUsesCrlfParagraphBreaks_ProducesTwoParagraphsAndNoStrayCr()
    {
        // Arrange — drafts saved from the Razor textarea use CRLF ("\r\n\r\n") between paragraphs
        var mail = new OutreachEmail("ceo@acme.com", "s", "First paragraph.\r\n\r\nSecond paragraph.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(Opts(), mail);

        // Assert
        var htmlParagraphCount = System.Text.RegularExpressions.Regex.Matches(msg.HtmlBody!, "<p ").Count;
        htmlParagraphCount.Should().Be(2);
        msg.HtmlBody.Should().Contain("First paragraph.");
        msg.HtmlBody.Should().Contain("Second paragraph.");
        msg.HtmlBody.Should().NotContain("\r");
    }
}
