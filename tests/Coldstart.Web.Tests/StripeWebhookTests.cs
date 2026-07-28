using System.Net;
using Coldstart.Application.Billing;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class StripeWebhookTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private async Task<CustomerId> SeedCustomerAsync(Action<Customer>? mutate = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var customer = Customer.Create($"webhook-co-{Guid.NewGuid():N}");
        mutate?.Invoke(customer);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<Customer> ReloadAsync(CustomerId id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        return await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == id);
    }

    private Task<HttpResponseMessage> PostWebhookAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", "t=1,v1=fake");
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Webhook_WithBadSignature_Returns400_AndNoPlanChange()
    {
        // Arrange
        var client = factory.CreateClient();
        var id = await SeedCustomerAsync();
        factory.StripeEvents.Next = new SubscriptionChange("cus_bad", "sub_bad", SubscriptionState.Active, null, id.Value);
        factory.StripeEvents.Throw = new InvalidOperationException("simulated bad signature");

        // Act
        var response = await PostWebhookAsync(client);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReloadAsync(id)).Plan.Should().Be(CustomerPlan.Free);
    }

    [Fact]
    public async Task Webhook_WithActiveSubscription_FlipsCustomerToFull()
    {
        // Arrange — first activation: no stored StripeCustomerId, mapped via CustomerRef
        var client = factory.CreateClient();
        var id = await SeedCustomerAsync();
        var stripeCustomerId = $"cus_{Guid.NewGuid():N}";
        factory.StripeEvents.Throw = null;
        factory.StripeEvents.Next = new SubscriptionChange(stripeCustomerId, "sub_1", SubscriptionState.Active,
            DateTimeOffset.UtcNow.AddDays(30), id.Value);

        // Act
        var response = await PostWebhookAsync(client);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var customer = await ReloadAsync(id);
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.StripeCustomerId.Should().Be(stripeCustomerId);
    }

    [Fact]
    public async Task Webhook_WithCanceled_FlipsToFree()
    {
        // Arrange — an already-Full customer, mapped by stored StripeCustomerId
        var client = factory.CreateClient();
        var stripeCustomerId = $"cus_{Guid.NewGuid():N}";
        var id = await SeedCustomerAsync(c =>
        {
            c.LinkStripeCustomer(stripeCustomerId);
            c.ActivateFull("sub_1", DateTimeOffset.UtcNow.AddDays(30));
        });
        factory.StripeEvents.Throw = null;
        factory.StripeEvents.Next = new SubscriptionChange(stripeCustomerId, "sub_1", SubscriptionState.Canceled, null, null);

        // Act
        var response = await PostWebhookAsync(client);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReloadAsync(id)).Plan.Should().Be(CustomerPlan.Free);
    }
}
