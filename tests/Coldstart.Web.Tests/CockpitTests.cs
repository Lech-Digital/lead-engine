using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class CockpitTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Cockpit_AsSuperAdmin_ShowsVitalsAndCustomerTable()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"cockpit-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var html = await client.GetStringAsync("/admin/cockpit");

        // Assert — "Paying customers" is the .pz-stat label (sentence case, replaces the old
        // lowercase "paying customers" span text after the .pz redesign).
        html.Should().Contain("Cockpit");
        html.Should().Contain("Paying customers");
        html.Should().Contain("Engine health per customer");
    }

    [Fact]
    public async Task Cockpit_FailedHarvest_ShowsHarvestFailedFlagOnAlarmRow()
    {
        // Arrange — a Full-plan customer whose last harvest run failed outright.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var name = $"Failed Co {Guid.NewGuid():N}";
        var customer = Customer.Create(name);
        customer.UpdateProfile(CompanyProfile.Empty with { });
        customer.SetPlan(CustomerPlan.Full);
        customer.MarkHarvest(HarvestStatus.Failed, DateTimeOffset.UtcNow);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"cockpit-fail-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var html = await client.GetStringAsync("/admin/cockpit");

        // Assert — the row for this customer carries the alarm highlight and the specific
        // "Harvest failed" reason, not just a generic warning icon.
        html.Should().Contain(name);
        html.Should().Contain("pz-row-alarm");
        html.Should().Contain("Harvest failed");
    }

    [Fact]
    public async Task Cockpit_StaleHarvest_ShowsStaleFlagOnAlarmRow()
    {
        // Arrange — a Full-plan customer whose engine last ran 10 days ago (well past the 36h
        // freshness threshold), even though the run itself reported success.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var name = $"Stale Co {Guid.NewGuid():N}";
        var customer = Customer.Create(name);
        customer.UpdateProfile(CompanyProfile.Empty with { });
        customer.SetPlan(CustomerPlan.Full);
        customer.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow.AddDays(-10));
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"cockpit-stale-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var html = await client.GetStringAsync("/admin/cockpit");

        // Assert — Razor HTML-encodes the em dash and plus sign to numeric entities, so match on
        // the plain-ASCII portions of the copy rather than the literal "—"/"+" characters.
        html.Should().Contain(name);
        html.Should().Contain("pz-row-alarm");
        html.Should().Contain("Stale").And.Contain("no harvest in 36h");
    }

    [Fact]
    public async Task Cockpit_RendersAdminRail()
    {
        // Arrange — SuperAdmin session
        var client = factory.CreateClient();
        var email = $"cockpit-rail-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var html = await client.GetStringAsync("/admin/cockpit");

        // Assert
        html.Should().Contain("id=\"app-rail\"");
        html.Should().Contain(">Cockpit<");
        html.Should().Contain("stats.getcoldstart.nl");
        html.Should().NotContain("settings-nav");
    }

    [Fact]
    public async Task Cockpit_MarksCockpitItemActive_NotOtherItems()
    {
        // Arrange — SuperAdmin session
        var client = factory.CreateClient();
        var email = $"cockpit-active-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var html = await client.GetStringAsync("/admin/cockpit");

        // Assert — the Cockpit item, and only the Cockpit item, carries "active"
        html.Should().Contain("class=\"rail-item active\" href=\"/admin/cockpit\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/admin\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/admin/mailbox\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/admin/exclusions\"");
        System.Text.RegularExpressions.Regex.Matches(html, "rail-item active").Count.Should().Be(1);
    }

    [Fact]
    public async Task Exclusions_MarksExclusionsItemActive_NotOtherItems()
    {
        // Arrange — SuperAdmin session
        var client = factory.CreateClient();
        var email = $"excl-active-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var html = await client.GetStringAsync("/admin/exclusions");

        // Assert — the Exclusions item, and only the Exclusions item, carries "active"
        html.Should().Contain("class=\"rail-item active\" href=\"/admin/exclusions\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/admin/cockpit\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/admin\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/admin/mailbox\"");
        System.Text.RegularExpressions.Regex.Matches(html, "rail-item active").Count.Should().Be(1);
    }

    [Fact]
    public async Task Cockpit_AsRegularCustomer_IsDenied()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"cockpit-user-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var response = await client.GetAsync("/admin/cockpit");

        // Assert
        ((int)response.StatusCode).Should().BeOneOf(302, 403, 404);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Engine health");
    }

    [Fact]
    public async Task Cockpit_WhenSignedOut_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/admin/cockpit");

        // Assert
        ((int)response.StatusCode).Should().BeOneOf(302, 401);
    }
}
