using Coldstart.Web.Email;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class EmailTemplatesTests
{
    private const string Url = "https://getcoldstart.nl/Identity/Account/ResetPassword?code=abc&email=x%40y.com";

    [Fact]
    public void Invitation_IncludesCompanyLinkAndBrandInBothBodies()
    {
        // Act
        var c = EmailTemplates.Invitation("Acme Co", Url);

        // Assert
        c.Subject.Should().NotBeNullOrWhiteSpace();
        c.TextBody.Should().Contain(Url).And.Contain("Acme Co");
        c.HtmlBody.Should().Contain(Url).And.Contain("Acme Co").And.Contain("getcoldstart.nl");
        c.HtmlBody.Should().Contain("coldstart<span");
        c.HtmlBody.Should().Contain("#FF6A3D");
        c.HtmlBody.Should().NotContain("<img");
    }

    [Fact]
    public void PasswordReset_IncludesLinkAndBrandInBothBodies()
    {
        // Act
        var c = EmailTemplates.PasswordReset(Url);

        // Assert
        c.TextBody.Should().Contain(Url);
        c.HtmlBody.Should().Contain(Url).And.Contain("getcoldstart.nl");
    }

    [Fact]
    public void Shell_IsMobileResponsive_FluidWidthAndMediaQuery()
    {
        // Act
        var html = EmailTemplates.PasswordReset(Url).HtmlBody;

        // Assert — fluid up to a 600px cap, with a small-screen media query; no fixed pixel width
        // attribute (which is what makes an email render "very wide" / overflow on phones).
        html.Should().Contain("width=device-width").And.Contain("initial-scale=1"); // viewport
        html.Should().Contain("max-width:600px");
        html.Should().Contain("@media");
        html.Should().NotContain("width=\"600\""); // no fixed-width attribute
    }

    [Fact]
    public void Invitation_HtmlEncodesCompanyName()
    {
        // Act — a company name with HTML-significant characters must not inject raw markup
        var c = EmailTemplates.Invitation("A & B <Ltd>", Url);

        // Assert
        c.HtmlBody.Should().Contain("A &amp; B &lt;Ltd&gt;").And.NotContain("<Ltd>");
    }

    [Fact]
    public void Invitation_ExplainsWhatColdstartIs()
    {
        // Arrange + Act
        var content = EmailTemplates.Invitation("Acme Co", "https://example.com/set");

        // Assert — first-touchpoint explanation appears in both bodies
        content.TextBody.Should().Contain("finds companies that need what you sell");
        content.HtmlBody.Should().Contain("finds companies that need what you sell");
    }

    [Fact]
    public void Welcome_ExplainsNextSteps_AndLinksToTheApp()
    {
        // Arrange + Act
        var content = EmailTemplates.Welcome("https://getcoldstart.nl/app");

        // Assert
        content.Subject.Should().Contain("Welcome");
        content.TextBody.Should().Contain("https://getcoldstart.nl/app");
        content.HtmlBody.Should().Contain("https://getcoldstart.nl/app");
        content.TextBody.Should().Contain("morning");
    }

    [Fact]
    public void MorningBriefing_ListsLeadsAndLinksToTheApp()
    {
        // Arrange
        var leads = new[] { new EmailTemplates.BriefingLead("Acme Co", "A thought on your new funding"),
                            new EmailTemplates.BriefingLead("Beta BV", "Your first sales hire") };

        // Act
        var content = EmailTemplates.MorningBriefing(2, leads, "https://getcoldstart.nl/app");

        // Assert
        content.Subject.Should().Contain("2 new leads");
        content.TextBody.Should().Contain("Acme Co").And.Contain("Beta BV");
        content.HtmlBody.Should().Contain("Acme Co").And.Contain("A thought on your new funding");
        content.HtmlBody.Should().Contain("https://getcoldstart.nl/app");
        content.HtmlBody.Should().NotContain("<img");
    }

    [Fact]
    public void MorningBriefing_HtmlEncodesLeadFields()
    {
        // Arrange
        var leads = new[] { new EmailTemplates.BriefingLead("A & B <Ltd>", "Re: <script>") };

        // Act
        var content = EmailTemplates.MorningBriefing(1, leads, "https://getcoldstart.nl/app");

        // Assert
        content.Subject.Should().Contain("1 new lead waiting");
        content.HtmlBody.Should().Contain("A &amp; B &lt;Ltd&gt;");
        content.HtmlBody.Should().NotContain("<script>");
    }
}
