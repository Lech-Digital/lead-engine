using System.Net;
using Coldstart.Application.Hints;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class OnboardingHintsTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task App_FirstVisit_RendersFirstHint_WithDismissForm()
    {
        // Arrange — a signed-in user with a customer (mirrors CustomerZoneTests setup)
        var client = factory.CreateClient();
        var email = $"hint-first-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        var decoded = WebUtility.HtmlDecode(html);
        decoded.Should().Contain(OnboardingHints.All[0].Body);
        html.Should().Contain("handler=DismissHint").And.Contain("__RequestVerificationToken");
        html.Should().Contain(OnboardingHints.All[0].Key);   // hint key in the form
    }

    [Fact]
    public async Task DismissHint_HidesItSameDay_AndShowsNoSecondHint()
    {
        // Arrange — signed-in client; scrape token from GET /app
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"hint-dismiss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        var token = await TestAuth.AntiforgeryAsync(client, "/app");

        // Act
        var post = await client.PostAsync("/app?handler=DismissHint", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token!, ["key"] = OnboardingHints.All[0].Key }));
        var html = await client.GetStringAsync("/app");

        // Assert — PRG redirect, and same-day the strip is gone with no second hint promoted
        post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        var decoded = WebUtility.HtmlDecode(html);
        decoded.Should().NotContain(OnboardingHints.All[0].Body);
        decoded.Should().NotContain(OnboardingHints.All[1].Body);
    }

    [Fact]
    public async Task App_WhenNoCustomer_DoesNotRenderHintOrThrow()
    {
        // Arrange — a signed-in user WITHOUT a provisioned customer (CustomerId null)
        var client = factory.CreateClient();
        var email = $"hint-nocustomer-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!", provisionTenant: false);
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");

        // Act
        var response = await client.GetAsync("/app");

        // Assert — page still 200s; no hint strip
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("handler=DismissHint");
    }
}
