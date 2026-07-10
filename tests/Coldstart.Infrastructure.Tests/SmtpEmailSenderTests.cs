using Coldstart.Infrastructure.Email;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class SmtpEmailSenderTests
{
    private static readonly SmtpOptions Opts = new()
    {
        Host = "smtp.example.com", Port = 465, Username = "u", Password = "p",
        FromName = "Coldstart", FromAddress = "noreply@getcoldstart.nl",
    };

    [Fact]
    public void BuildMessage_WithHtml_ProducesMultipartWithBothParts()
    {
        // Act
        var msg = SmtpEmailSender.BuildMessage(Opts, "to@x.com", "Subj", "plain text link", "<p>html link</p>");

        // Assert
        msg.From.ToString().Should().Contain("noreply@getcoldstart.nl");
        msg.Subject.Should().Be("Subj");
        msg.TextBody.Should().Contain("plain text link");
        msg.HtmlBody.Should().Contain("html link");
    }

    [Fact]
    public void BuildMessage_WithoutHtml_ProducesPlainTextOnly()
    {
        // Act
        var msg = SmtpEmailSender.BuildMessage(Opts, "to@x.com", "Subj", "plain only", null);

        // Assert
        msg.TextBody.Should().Be("plain only");
        msg.HtmlBody.Should().BeNull();
    }

    [Fact]
    public void BuildMessage_WithBlankFromAddress_FallsBackToUsername()
    {
        // Arrange
        var opts = new SmtpOptions { Username = "user@x.com", FromAddress = "", FromName = "C" };

        // Act
        var msg = SmtpEmailSender.BuildMessage(opts, "to@x.com", "S", "t", null);

        // Assert
        msg.From.ToString().Should().Contain("user@x.com");
    }
}
