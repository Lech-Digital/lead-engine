using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class NightlyHarvestTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    // Note: every ColdstartWebFactory-backed test also carries an ambient "Coldstart" dogfood
    // tenant (seeded + promoted to Full plan by IdentitySeeder at host startup, with a couple of
    // unfinished leads) — so, like the existing AllAsync-based tests in this suite, we assert
    // Contain/NotContain rather than an exact set, to stay independent of that ambient row.
    [Fact]
    public async Task IdsDueForHarvest_ReturnsOnlyFull_NotRunning_BelowThreshold()
    {
        // Arrange — one customer per row of the policy matrix (threshold = 5).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

        var dueLow = await SeedAsync(db, "due-low", CustomerPlan.Full, HarvestStatus.Done, pending: 4);   // due
        var dueEmpty = await SeedAsync(db, "due-empty", CustomerPlan.Full, HarvestStatus.None, pending: 0); // due
        var dueOnlySent = await SeedAsync(db, "due-sent", CustomerPlan.Full, HarvestStatus.Done, pending: 0, sent: 3); // due (0 unfinished)
        var atCap = await SeedAsync(db, "full-atcap", CustomerPlan.Full, HarvestStatus.Done, pending: 5);   // skip (== threshold)
        var over = await SeedAsync(db, "full-over", CustomerPlan.Full, HarvestStatus.Done, pending: 15);    // skip
        var running = await SeedAsync(db, "full-running", CustomerPlan.Full, HarvestStatus.Running, pending: 0); // skip (in-flight)
        var free = await SeedAsync(db, "free-low", CustomerPlan.Free, HarvestStatus.Done, pending: 0);      // skip (free)

        // Act
        var due = await repo.IdsDueForHarvestAsync(5, default);

        // Assert
        due.Should().Contain(new[] { dueLow, dueEmpty, dueOnlySent });
        due.Should().NotContain(new[] { atCap, over, running, free });
    }

    // Approved-but-not-sent counts as unfinished too (same as the briefing), so it gates like Pending.
    [Fact]
    public async Task IdsDueForHarvest_CountsApprovedAsUnfinished()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var id = await SeedAsync(db, "appr", CustomerPlan.Full, HarvestStatus.Done, pending: 0, approved: 5); // 5 unfinished

        // Act
        var due = await repo.IdsDueForHarvestAsync(5, default);

        // Assert
        due.Should().NotContain(id);
    }

    // Seeds a customer (plan + harvest status) and the requested number of leads in each status.
    private static async Task<Guid> SeedAsync(ColdstartDbContext db, string name, CustomerPlan plan,
        HarvestStatus status, int pending = 0, int approved = 0, int sent = 0)
    {
        var customer = Customer.Create(name);
        customer.UpdateProfile(CompanyProfile.Empty with { }); // distinct owned-entity instance per customer;
                                                                // Customer.Profile defaults to the shared static
                                                                // Empty singleton, which two Adds in one DbContext
                                                                // cannot both own (owned entity, shared PK).
        customer.SetPlan(plan);
        customer.MarkHarvest(status, status == HarvestStatus.Running ? null : DateTimeOffset.UtcNow);
        db.Customers.Add(customer);
        void AddLeads(int n, Action<Lead> mutate)
        {
            for (var i = 0; i < n; i++)
            {
                var lead = Lead.CreateHarvested(customer.Id, $"{name}-co-{Guid.NewGuid():N}", Fit.Yes,
                    "subj", "draft", "src", $"https://x/{Guid.NewGuid():N}", Guid.NewGuid().ToString("N"),
                    0.7, "sig", DateTimeOffset.UtcNow);
                mutate(lead);
                db.Leads.Add(lead);
            }
        }
        AddLeads(pending, _ => { });                        // PendingReview (default)
        AddLeads(approved, l => l.Approve());
        AddLeads(sent, l => { l.Approve(new EmailAddress("a@b.com")); l.MarkSent(DateTimeOffset.UtcNow); });
        await db.SaveChangesAsync();
        return customer.Id.Value;
    }
}
