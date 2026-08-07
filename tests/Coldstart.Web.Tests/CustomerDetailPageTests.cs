using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

// The customer detail page (/admin/customers/{id}) — the operator's one place to manage a single
// customer. Grant/cancel/harvest-enqueue/harvest-refusal behaviour is already covered end-to-end
// against this page in AdminConsoleComplimentaryAndHarvestTests; this file covers the page's own
// concerns: rendering each plan state correctly, auth, and 404 for an unknown id.
public class CustomerDetailPageTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Detail_ForNonSuperAdmin_IsDenied()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plain-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");   // no role

        // Act
        var response = await client.GetAsync($"/admin/customers/{Guid.NewGuid()}");

        // Assert — not a SuperAdmin => bounced to login, never reaches the page
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Detail_UnknownCustomerId_Returns404()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var response = await client.GetAsync($"/admin/customers/{Guid.NewGuid()}");

        // Assert — a genuine 404, not an unhandled exception or a silent redirect
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Detail_FreePlanCustomer_ShowsFreePlanState()
    {
        // Arrange — a freshly-invited customer is Free by default
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"free-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Free Co");

        // Act
        var html = await client.GetStringAsync($"/admin/customers/{customerId}");

        // Assert
        html.Should().Contain("Free plan");
        html.Should().NotContain("Full plan via Stripe");
        html.Should().NotContain("Complimentary — full access");
    }

    [Fact]
    public async Task Detail_PaidStripeCustomer_ShowsRenewalDate()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"paid-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Paid Co");
        var renewsAt = DateTimeOffset.UtcNow.AddDays(20);
        await MutateCustomerAsync(customerId, c => c.ActivateFull("sub_live_1", renewsAt));

        // Act
        var html = await client.GetStringAsync($"/admin/customers/{customerId}");

        // Assert — states the truth plainly: paid via Stripe, with the actual renewal date
        html.Should().Contain("Full plan via Stripe");
        html.Should().Contain(renewsAt.ToString("d MMM yyyy"));
        html.Should().NotContain("Complimentary — full access");
    }

    [Fact]
    public async Task Detail_ComplimentaryCustomer_ShowsEndDateAndDaysRemaining()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"comp-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Comp Co");
        var until = DateTimeOffset.UtcNow.AddDays(5);
        await MutateCustomerAsync(customerId, c => c.GrantComplimentary(until, DateTimeOffset.UtcNow));

        // Act
        var html = await client.GetStringAsync($"/admin/customers/{customerId}");

        // Assert — reads as its own state (not indistinguishable from a real paying customer),
        // with both the end date and a days-remaining count
        html.Should().Contain("Complimentary — full access until");
        html.Should().Contain(until.ToString("d MMM yyyy"));
        html.Should().Contain("5 days left");
        html.Should().NotContain("Full plan via Stripe");

        // And the grant control is unmistakable that a new grant replaces rather than extends
        html.Should().Contain("replaces").And.Contain("does not add to it");
        html.Should().Contain("Cancel complimentary access");
    }

    [Fact]
    public async Task Detail_ShowsTheCustomersTargetingFields()
    {
        // Arrange — SuperAdmin session and a customer
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"targeting-view-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Targeting View Co");

        // Act — GET /admin/customers/{id}
        var html = await client.GetStringAsync($"/admin/customers/{customerId}");

        // Assert
        html.Should().Contain("Targeting");
        html.Should().Contain("Industries");
    }

    [Fact]
    public async Task Detail_SaveTargeting_PersistsAndIsVisibleToTheCustomersOwnPage()
    {
        // Arrange — SuperAdmin saves targeting for a customer
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"targeting-save-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Targeting Save Co");

        // Act — POST the admin targeting form, then read it back via the same use case
        // (IGetTargeting) that the customer's own settings page uses
        await TestAuth.PostFormAsync(client, $"/admin/customers/{customerId}?handler=SaveTargeting", new()
        {
            ["Targeting.Industries"] = "SaaS\nFintech",
            ["Targeting.Geographies"] = "Netherlands",
            ["Targeting.BuyerRoles"] = "CTO",
            ["Targeting.SizeMin"] = "50",
            ["Targeting.SizeMax"] = "500",
            ["Targeting.TriggerEvents"] = "Hiring",
            ["Targeting.Exclusions"] = "Competitors",
        });

        var view = await ReadTargetingAsync(customerId);

        // Assert — the stored values match what the admin entered, proving both surfaces
        // share one write path
        view.Should().NotBeNull();
        view!.Industries.Should().Be("SaaS\nFintech");
        view.Geographies.Should().Be("Netherlands");
        view.BuyerRoles.Should().Be("CTO");
        view.SizeMin.Should().Be(50);
        view.SizeMax.Should().Be(500);
        view.TriggerEvents.Should().Be("Hiring");
        view.Exclusions.Should().Be("Competitors");
    }

    [Fact]
    public async Task Detail_SaveTargetingWithInvertedSizeBand_ShowsTheErrorAndSavesNothing()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"targeting-invalid-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Targeting Invalid Co");

        // Act
        var html = await TestAuth.PostFormReadAsync(client, $"/admin/customers/{customerId}?handler=SaveTargeting", new()
        {
            ["Targeting.SizeMin"] = "500",
            ["Targeting.SizeMax"] = "50",
        });

        var view = await ReadTargetingAsync(customerId);

        // Assert — the "smallest company size" message appears and nothing was stored
        html.Should().Contain("smallest company size");
        view.Should().NotBeNull();
        view!.SizeMin.Should().BeNull();
        view.SizeMax.Should().BeNull();
    }

    // Reads targeting back via IGetTargeting — the same use case the customer's own settings
    // page (TargetingSettingsModel) calls. Its underlying repository read is tenant-scoped, so
    // (like the admin page itself) this establishes the tenant for the scope first.
    private async Task<TargetingView?> ReadTargetingAsync(Guid customerId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantScope>().SetCurrent(customerId);
        var getTargeting = scope.ServiceProvider.GetRequiredService<IGetTargeting>();
        return await getTargeting.ForCustomerAsync(customerId, CancellationToken.None);
    }

    private async Task<Guid> InviteAndGetCustomerIdAsync(HttpClient client, string invitee, string company)
    {
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite",
            new() { ["Input.Company"] = company, ["Input.Email"] = invitee, ["Input.Niche"] = "test niche" });
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await mgr.FindByEmailAsync(invitee))!.CustomerId!.Value;
    }

    private async Task MutateCustomerAsync(Guid customerId, Action<Customer> mutate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var customer = (await db.Customers.IgnoreQueryFilters().ToListAsync()).Single(c => c.Id.Value == customerId);
        mutate(customer);
        await db.SaveChangesAsync();
    }
}
