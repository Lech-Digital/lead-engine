using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coldstart.Application.Billing;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public sealed class AdminApiComplimentaryTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    private async Task<Guid> InviteAsync(HttpClient client, string email, string company)
    {
        var invite = await client.PostAsJsonAsync("/api/admin/invites", new { email, company, niche = "n" });
        return (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();
    }

    // Invited users start unconfirmed; the grant email only goes to a confirmed address.
    private async Task ConfirmAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user!.EmailConfirmed = true;
        await users.UpdateAsync(user);
    }

    // Same mechanism as StripeWebhookTests: drive a customer to a live Stripe subscription
    // through the real webhook endpoint, with the Stripe event parser faked.
    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", "t=1,v1=fake");
        return client.SendAsync(request);
    }

    private async Task<Customer> ReloadCustomerAsync(Guid customerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var all = await db.Customers.IgnoreQueryFilters().ToListAsync();
        return all.Single(c => c.Id.Value == customerId);
    }

    [Fact]
    public async Task PostComplimentary_ConfirmedCustomer_GrantsAndSendsEmail()
    {
        // Arrange
        using var client = Client();
        var email = "comp-happy@example.com";
        var customerId = await InviteAsync(client, email, "Comp Happy BV");
        await ConfirmAsync(email);
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("emailSent").GetBoolean().Should().BeTrue();
        body.GetProperty("until").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddDays(4));
        factory.Email.Sent.Skip(before).Should().Contain(m => m.To == email && m.Subject.Contains("full plan"));
    }

    [Fact]
    public async Task PostComplimentary_UnconfirmedCustomer_StillGrantsButReportsNoEmail()
    {
        // Arrange — invited, never set a password, so no confirmed address to mail
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-pending@example.com", "Comp Pending BV");

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

        // Assert — the grant stands; only the notification is missing
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("emailSent").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PostComplimentary_UnknownCustomer_Returns404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostComplimentary_ZeroDays_Returns400()
    {
        // Arrange
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-zero@example.com", "Comp Zero BV");

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 0 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostComplimentary_CustomerWithActiveSubscription_Returns400()
    {
        // Arrange — flip the customer to a live Stripe subscription through the real webhook
        // endpoint first (FakeStripeEventParser / FakeBillingGateway, same tools BillingFlowTests
        // and StripeWebhookTests already use), then try to grant complimentary access on top of it.
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-paid@example.com", "Comp Paid BV");
        var stripeCustomerId = $"cus_{Guid.NewGuid():N}";
        factory.StripeEvents.Throw = null;
        factory.StripeEvents.Next = new SubscriptionChange(stripeCustomerId, "sub_1", SubscriptionState.Active,
            DateTimeOffset.UtcNow.AddDays(30), customerId);
        var webhook = await PostWebhookAsync(client);
        webhook.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReloadCustomerAsync(customerId)).Plan.Should().Be(CustomerPlan.Full);

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("already has a paid subscription");
    }

    [Fact]
    public async Task PostComplimentary_EmailSendFails_StillGrantsButReportsNoEmail()
    {
        // Arrange — the mail transport is unreachable; the grant must stand regardless.
        using var client = Client();
        var email = "comp-unreachable@example.com";
        var customerId = await InviteAsync(client, email, "Comp Unreachable BV");
        await ConfirmAsync(email);
        factory.Email.Unreachable = true;
        try
        {
            // Act
            var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

            // Assert — 200, no email reported, but the grant persisted
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("emailSent").GetBoolean().Should().BeFalse();

            var customer = await ReloadCustomerAsync(customerId);
            customer.Plan.Should().Be(CustomerPlan.Full);
            customer.ComplimentaryUntil.Should().NotBeNull();
        }
        finally
        {
            // Reset so suite ordering can't leak this into a later test.
            factory.Email.Unreachable = false;
        }
    }

    [Fact]
    public async Task PostComplimentary_WithoutAdminKey_IsRejected()
    {
        // Arrange — no X-Admin-Key header
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
