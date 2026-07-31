using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class BillingFlowTests(FoilWebFactory factory) : IClassFixture<FoilWebFactory>
{
    private async Task<Guid> CustomerIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.CustomerId!.Value;
    }

    private async Task AddLeadsAsync(string email, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var cid = new CustomerId(await CustomerIdAsync(email));
        for (var i = 0; i < count; i++)
            db.Leads.Add(Lead.CreateHarvested(cid, $"Co{i}", Fit.Yes, $"subj{i}", "body",
                "hackernews", "https://x/1", $"fp-{Guid.NewGuid():N}", 0.9 - i * 0.01, "outbound", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Checkout_ForSignedInFreeCustomer_RedirectsToStripe()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"billing-checkout-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var customerId = await CustomerIdAsync(email);

        // Act
        var response = await TestAuth.PostFormAsync(client, "/app/billing?handler=Checkout", new());

        // Assert
        ((int)response.StatusCode).Should().BeInRange(300, 399);
        response.Headers.Location!.OriginalString.Should().Be(factory.Billing.CheckoutUrl);
        factory.Billing.LastCheckout.Should().NotBeNull();
        factory.Billing.LastCheckout!.CustomerId.Should().Be(customerId);
    }

    [Fact]
    public async Task Checkout_WithSupportedCurrency_PassesItToTheGateway()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"billing-usd-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var response = await TestAuth.PostFormAsync(client, "/app/billing?handler=Checkout",
            new() { ["currency"] = "USD" });

        // Assert — normalized to lowercase for Stripe
        ((int)response.StatusCode).Should().BeInRange(300, 399);
        factory.Billing.LastCheckout!.Currency.Should().Be("usd");
    }

    [Fact]
    public async Task Checkout_WithUnsupportedCurrency_FallsBackToDefault()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"billing-badcur-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var response = await TestAuth.PostFormAsync(client, "/app/billing?handler=Checkout",
            new() { ["currency"] = "btc" });

        // Assert — bogus input never reaches Stripe; the session falls back to the price default
        ((int)response.StatusCode).Should().BeInRange(300, 399);
        factory.Billing.LastCheckout!.Currency.Should().BeNull();
    }

    [Fact]
    public async Task Portal_WithoutStripeCustomer_DoesNotRedirectToStripe()
    {
        // Arrange — a fresh Free customer has no StripeCustomerId yet
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"billing-portal-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var response = await TestAuth.PostFormAsync(client, "/app/billing?handler=Portal", new());

        // Assert — sent back into the app, not to Stripe
        ((int)response.StatusCode).Should().BeInRange(300, 399);
        response.Headers.Location!.OriginalString.Should().NotBe(factory.Billing.PortalUrl);
        response.Headers.Location!.OriginalString.Should().StartWith("/app");
    }

    [Fact]
    public async Task Billing_Endpoints_RequireAuth()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.PostAsync("/app/billing?handler=Checkout",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Foil_UpgradeCta_PostsToCheckout()
    {
        // Arrange — 5 leads, quota 2 → locked rows + foil CTA present
        var client = factory.CreateClient();
        var email = $"billing-foil-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        await AddLeadsAsync(email, 5);

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert — the upgrade path is a checkout form, not a mailto
        html.Should().Contain("/app/billing?handler=Checkout");
        html.Should().NotContain("mailto:andrzej@getcoldstart.nl");
    }
}
