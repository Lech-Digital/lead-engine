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
        c.HtmlBody.Should().Contain("Cold").And.Contain("start"); // wordmark
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
    public void Invitation_HtmlEncodesCompanyName()
    {
        // Act — a company name with HTML-significant characters must not inject raw markup
        var c = EmailTemplates.Invitation("A & B <Ltd>", Url);

        // Assert
        c.HtmlBody.Should().Contain("A &amp; B &lt;Ltd&gt;").And.NotContain("<Ltd>");
    }
}
