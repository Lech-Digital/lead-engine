using Coldstart.Infrastructure.Mailbox;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class MailHtmlSanitizerTests
{
    [Fact]
    public void Sanitize_WhenHtmlContainsScript_StripsIt()
    {
        // Arrange
        var html = "<p>hello</p><script>alert(1)</script>";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("hello");
        result.Html.Should().NotContain("script").And.NotContain("alert");
    }

    [Fact]
    public void Sanitize_WhenElementHasEventHandler_StripsTheHandler()
    {
        // Arrange
        var html = """<img src="cid:logo" onerror="alert(1)"><b onclick="x()">hi</b>""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().NotContain("onerror").And.NotContain("onclick");
        result.Html.Should().Contain("hi");
    }

    [Fact]
    public void Sanitize_WhenImageIsRemote_RemovesItAndFlags()
    {
        // Arrange
        var html = """<p>text</p><img src="https://tracker.example/pixel.png">""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().NotContain("tracker.example");
        result.RemoteImagesRemoved.Should().BeTrue();
    }

    [Fact]
    public void Sanitize_WhenImageIsInlineCid_KeepsIt()
    {
        // Arrange
        var html = """<img src="cid:part1.abc">""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("cid:part1.abc");
        result.RemoteImagesRemoved.Should().BeFalse();
    }

    [Fact]
    public void Sanitize_WhenLinkIsJavascript_NeutralizesIt()
    {
        // Arrange
        var html = """<a href="javascript:alert(1)">click</a>""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("click");
    }

    [Fact]
    public void Sanitize_WhenLinkIsHttps_ForcesNewTabAndNoopener()
    {
        // Arrange
        var html = """<a href="https://example.com">site</a>""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("https://example.com");
        result.Html.Should().Contain("target=\"_blank\"");
        result.Html.Should().Contain("noopener");
    }

    [Fact]
    public void Sanitize_PlainFormatting_SurvivesUntouched()
    {
        // Arrange
        var html = "<p>Hi <strong>there</strong>,</p><ul><li>one</li></ul><table><tr><td>x</td></tr></table>";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("<strong>there</strong>");
        result.Html.Should().Contain("<li>one</li>");
        result.Html.Should().Contain("<td>x</td>");
    }
}
