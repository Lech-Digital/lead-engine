using System.Net;
using Coldstart.Application.Hints;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
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
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

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
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
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
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long", provisionTenant: false);
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var response = await client.GetAsync("/app");

        // Assert — page still 200s; no hint strip
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("handler=DismissHint");
    }

    [Fact]
    public async Task ShowHintsAgain_AfterDismissingAll_RestoresFirstHintOnApp()
    {
        // Arrange — signed-in client that has dismissed the first hint already
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"hint-showhints-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var dismissToken = await TestAuth.AntiforgeryAsync(client, "/app");
        await client.PostAsync("/app?handler=DismissHint", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = dismissToken!, ["key"] = OnboardingHints.All[0].Key }));

        // Act — click "Show hints again" on the profile page
        var resetToken = await TestAuth.AntiforgeryAsync(client, "/app/settings/account");
        var reset = await client.PostAsync("/app/settings/account?handler=ShowHints", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = resetToken! }));
        var appHtml = await client.GetStringAsync("/app");

        // Assert
        reset.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        var decoded = WebUtility.HtmlDecode(appHtml);
        decoded.Should().Contain(OnboardingHints.All[0].Body);
    }

    [Fact]
    public async Task DismissHint_WithUnknownKey_DoesNotChangeStateOrSuppressHint()
    {
        // Arrange — signed-in client, no hints dismissed yet
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"hint-bogus-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var token = await TestAuth.AntiforgeryAsync(client, "/app");

        // Act — POST a key that isn't in the OnboardingHints catalogue
        var post = await client.PostAsync("/app?handler=DismissHint", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token!, ["key"] = "bogus-not-a-hint" }));
        var html = await client.GetStringAsync("/app");

        // Assert — PRG redirect still happens, but the bogus key is ignored: hint #1 still shows
        // and no state was persisted on the user.
        post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        var decoded = WebUtility.HtmlDecode(html);
        decoded.Should().Contain(OnboardingHints.All[0].Body);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user!.HintsSeenCsv.Should().BeNull();
    }

    [Fact]
    public async Task DismissHint_IsPerUser_OtherUserUnaffected()
    {
        // Arrange — two independent signed-in users, each with a provisioned customer
        var clientA = factory.CreateClient(new() { AllowAutoRedirect = false });
        var emailA = $"hint-a-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, clientA, emailA, "Passw0rd!long");
        var tokenA = await TestAuth.AntiforgeryAsync(clientA, "/app");

        var clientB = factory.CreateClient();
        var emailB = $"hint-b-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, clientB, emailB, "Passw0rd!long");

        // Act — user A dismisses hint #1
        await clientA.PostAsync("/app?handler=DismissHint", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = tokenA!, ["key"] = OnboardingHints.All[0].Key }));
        var htmlA = await clientA.GetStringAsync("/app");
        var htmlB = await clientB.GetStringAsync("/app");

        // Assert — A's dismissal doesn't leak into B's session
        var decodedA = WebUtility.HtmlDecode(htmlA);
        var decodedB = WebUtility.HtmlDecode(htmlB);
        decodedA.Should().NotContain(OnboardingHints.All[0].Body);
        decodedB.Should().Contain(OnboardingHints.All[0].Body);
    }

    [Fact]
    public async Task Explainer_HiddenAttribute_ResolvesToDisplayNone()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var css = await client.GetStringAsync("/css/site.css");

        // Assert — guards the regression that made "Got it" look dead (hidden explainer still visible)
        css.Should().Contain(".app-explainer[hidden]").And.Contain("display: none");
    }
}
