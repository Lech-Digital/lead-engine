using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class PublicPagesTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetLanding_ReturnsOk_WithBrandAndCta()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("coldstart");                        // wordmark
        html.Should().Contain("Get 3 sample leads");               // primary CTA
        html.Should().Contain("Wake up to your next client");      // hero promise
    }

    [Fact]
    public async Task GetLanding_TellsTheMorningStory_AndPricing()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("While you sleep");                  // night act
        html.Should().Contain("06:58");                            // timestamped story
        html.Should().Contain("€750");                             // transparent pricing
        html.Should().Contain("€350");                             // setup fee
        html.Should().Contain("doing the part AI is actually good at");   // AI section (static markup is emitted verbatim — avoid asserting on apostrophes)
    }

    [Fact]
    public async Task Landing_RendersScrollSceneScaffolding_AndKeepsActCopy()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert — both pinned scenes, all five night beats, the engine script, and key copy are served
        html.Should().Contain("scene--hero").And.Contain("scene--night");
        foreach (var b in new[] { "data-beat=\"0\"", "data-beat=\"1\"", "data-beat=\"2\"", "data-beat=\"3\"", "data-beat=\"4\"" })
            html.Should().Contain(b);
        html.Should().Contain("lp-scroll.js");
        html.Should().Contain("It's in your inbox.").And.Contain("What it costs. All of it.");
    }

    [Fact]
    public async Task GetLanding_HasHeroLoginLink_NotOnlyInStatusbar()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert — login lives in the sticky site nav now, visible without scrolling
        html.Should().Contain("site-nav-login");
        html.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task GetPrivacy_ReturnsOk_WithPolicyMarkers()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/privacy");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("Privacy");
        html.Should().Contain("Schiedam");
    }

    [Fact]
    public async Task GetTerms_ReturnsOk_WithDraftBanner()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/terms");

        // Assert
        html.Should().Contain("Terms");
        html.Should().Contain("pending review");
        html.Should().Contain("Netherlands");
    }

    [Fact]
    public async Task GetPrivacy_CoversGdprEssentials()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/privacy");

        // Assert
        html.Should().Contain("Autoriteit Persoonsgegevens");
        html.Should().Contain("Resend");
        html.Should().Contain("Hetzner");
        html.Should().Contain("Schiedam");
    }

    [Fact]
    public async Task Footer_AppearsOnPublicPages_WithRegistryPlaceholders()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var landing = await client.GetStringAsync("/");
        var privacy = await client.GetStringAsync("/privacy");

        // Assert
        landing.Should().Contain("KvK");
        privacy.Should().Contain("KvK");
        landing.Should().Contain("/terms");
        landing.Should().Contain("/privacy");
    }

    [Fact]
    public async Task Landing_WithCompanyQueryParam_PrefillsTheSampleLeadsForm()
    {
        // Arrange — outreach emails link to /?company=<name>#get-leads
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/?company=Acme%20%26%20Zn%20B.V.");

        // Assert — the company field arrives pre-filled (HTML-encoded by Razor)
        html.Should().Contain("value=\"Acme &amp; Zn B.V.\"");
    }

    [Fact]
    public async Task PublicPages_AboutExamplesTechnologyFaq_RenderWithNavAndTrustFacts()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var about = await client.GetStringAsync("/about");
        var examples = await client.GetStringAsync("/examples");
        var technology = await client.GetStringAsync("/technology");
        var faq = await client.GetStringAsync("/faq");
        var landing = await client.GetStringAsync("/");

        // Assert — each page carries the shared nav and its own substance
        about.Should().Contain("site-nav").And.Contain("KvK: 42125737").And.Contain("Andrzej");
        examples.Should().Contain("Real drafts").And.Contain("Bluecore Energy");
        technology.Should().Contain("public signals").And.Contain("human decides");
        faq.Should().Contain("<details>").And.Contain("3 free sample leads");
        landing.Should().Contain("site-nav").And.Contain("/examples");
    }

    [Fact]
    public async Task ContactPage_Post_SendsMessageToFounderMailbox()
    {
        // Arrange
        var client = factory.CreateClient();
        var page = await client.GetStringAsync("/contact");
        var token = System.Text.RegularExpressions.Regex.Match(page,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Curious Prospect", ["Email"] = "prospect@example.com",
            ["Message"] = "Is this real?", ["Website"] = "", ["__RequestVerificationToken"] = token,
        }));

        // Assert — PRG redirect and the message landed with the founder
        ((int)response.StatusCode).Should().BeInRange(200, 302);
        factory.Email.Sent.Skip(before).Should().Contain(m =>
            m.To == "info@getcoldstart.nl" && m.TextBody.Contains("Is this real?"));
    }

    [Fact]
    public async Task ContactPage_PostWithoutMessage_IsRejected()
    {
        // Arrange
        var client = factory.CreateClient();
        var page = await client.GetStringAsync("/contact");
        var token = System.Text.RegularExpressions.Regex.Match(page,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "X", ["Email"] = "x@example.com", ["Message"] = "", ["Website"] = "",
            ["__RequestVerificationToken"] = token,
        }));

        // Assert
        factory.Email.Sent.Count.Should().Be(before);
    }
}
