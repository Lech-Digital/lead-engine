using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

// Customer detail page controls: complimentary grant/cancel and manual harvest, alongside the
// existing Resend/Revoke/Block/Unblock/Remove row actions on /admin/customers/{id}.
public class AdminConsoleComplimentaryAndHarvestTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private async Task<Guid> InviteAndGetCustomerIdAsync(HttpClient client, string invitee, string company)
    {
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite",
            new() { ["Input.Company"] = company, ["Input.Email"] = invitee, ["Input.Niche"] = "test niche" });
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await mgr.FindByEmailAsync(invitee))!.CustomerId!.Value;
    }

    private async Task ConfirmAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await mgr.FindByEmailAsync(email);
        user!.EmailConfirmed = true;
        await mgr.UpdateAsync(user);
    }

    private async Task<Customer> ReloadCustomerAsync(Guid customerId)
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        return (await repo.AllAsync(CancellationToken.None)).Single(c => c.Id.Value == customerId);
    }

    private async Task SetHarvestStatusAsync(Guid customerId, HarvestStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var customer = (await repo.AllAsync(CancellationToken.None)).Single(c => c.Id.Value == customerId);
        customer.MarkHarvest(status, status == HarvestStatus.None ? null : DateTimeOffset.UtcNow);
        await repo.UpdateAsync(customer, CancellationToken.None);
    }

    [Fact]
    public async Task GrantComplimentary_ValidDays_GrantsAndEmailsConfirmedAdmin()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"grant-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Grant Co");
        await ConfirmAsync(invitee);
        var before = factory.Email.Sent.Count;

        // Act
        var html = await TestAuth.PostFormReadAsync(client, $"/admin/customers/{customerId}?handler=GrantComplimentary",
            new() { ["GrantDays"] = "5" });

        // Assert
        var customer = await ReloadCustomerAsync(customerId);
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.ComplimentaryUntil.Should().NotBeNull();
        factory.Email.Sent.Skip(before).Should().Contain(m => m.To == invitee);
        html.Should().Contain("Granted the full plan");
    }

    [Fact]
    public async Task CancelComplimentary_ActiveGrant_ReturnsToFreeWithoutEmailing()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"cancel-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Cancel Co");
        await TestAuth.PostFormAsync(client, $"/admin/customers/{customerId}?handler=GrantComplimentary",
            new() { ["GrantDays"] = "5" });
        var before = factory.Email.Sent.Count;

        // Act
        var html = await TestAuth.PostFormReadAsync(client, $"/admin/customers/{customerId}?handler=CancelComplimentary", new());

        // Assert
        var customer = await ReloadCustomerAsync(customerId);
        customer.Plan.Should().Be(CustomerPlan.Free);
        customer.ComplimentaryUntil.Should().BeNull();
        factory.Email.Sent.Count.Should().Be(before);
        html.Should().Contain("cancelled");
    }

    [Fact]
    public async Task RunHarvest_NotAlreadyPendingOrRunning_EnqueuesAndReportsQueuedNotFinished()
    {
        // Arrange — a freshly-invited customer starts Pending (auto-enqueued by provisioning),
        // so force it to Done first to exercise the "not already busy" path.
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"harvest-ok-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Harvest Ok Co");
        await SetHarvestStatusAsync(customerId, HarvestStatus.Done);
        var before = factory.HarvestQueue.Jobs.Count;

        // Act
        var html = await TestAuth.PostFormReadAsync(client, $"/admin/customers/{customerId}?handler=RunHarvest", new());

        // Assert — enqueued, and the message is honest about being asynchronous
        factory.HarvestQueue.Jobs.Skip(before).Should().Contain(j => j.CustomerId == customerId);
        html.Should().Contain("queued");
        html.Should().NotContain("finished");
    }

    [Fact]
    public async Task RunHarvest_AlreadyPending_IsGuardedAndDoesNotEnqueueAgain()
    {
        // Arrange — a freshly-invited customer is already Pending from provisioning's own
        // auto-enqueue; a second manual trigger must not pile up another job.
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"harvest-busy-{Guid.NewGuid():N}@test.com";
        var customerId = await InviteAndGetCustomerIdAsync(client, invitee, "Harvest Busy Co");
        (await ReloadCustomerAsync(customerId)).HarvestStatus.Should().Be(HarvestStatus.Pending);
        var before = factory.HarvestQueue.Jobs.Count;

        // Act
        var html = await TestAuth.PostFormReadAsync(client, $"/admin/customers/{customerId}?handler=RunHarvest", new());

        // Assert
        factory.HarvestQueue.Jobs.Count.Should().Be(before);
        html.Should().Contain("already pending");
    }
}
