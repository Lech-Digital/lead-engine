using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Email;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class WeeklyDigestTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Sweep_CustomerWithActivity_GetsBrandedDigest_AndOperatorHeartbeatAlwaysSends()
    {
        // Arrange — a customer with one sent lead this week
        var email = $"digest-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), "Weekly BV", Fit.Yes,
            "s", "d", "hackernews", "https://x", "fp-" + Guid.NewGuid().ToString("N"), 0.8, "outbound", DateTimeOffset.UtcNow.AddDays(-2));
        lead.Approve(new EmailAddress("ceo@x.com"));
        lead.MarkSent(DateTimeOffset.UtcNow.AddDays(-1));
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var sweeper = scope.ServiceProvider.GetRequiredService<WeeklyDigestSweeper>();
        var before = factory.Email.Sent.Count;

        // Act
        var (digests, operatorSent) = await sweeper.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert
        digests.Should().BeGreaterThanOrEqualTo(1);
        operatorSent.Should().BeTrue();
        factory.Email.Sent.Skip(before).Should().Contain(m => m.To == email && m.Subject.Contains("Your week with Coldstart"));
        factory.Email.Sent.Skip(before).Should().Contain(m => m.Subject.Contains("Coldstart weekly"));
    }

    [Fact]
    public async Task Sweep_CustomerWithNothingToReport_GetsNoEmail()
    {
        // Arrange — fresh customer, zero activity
        var email = $"digest-quiet-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long");
        using var scope = factory.Services.CreateScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<WeeklyDigestSweeper>();
        var before = factory.Email.Sent.Count;

        // Act
        await sweeper.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert — no digest addressed to the quiet customer
        factory.Email.Sent.Skip(before).Should().NotContain(m => m.To == email);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, "2026-07-31T10:00:00", 3)]   // Friday → next Monday
    [InlineData(DayOfWeek.Monday, "2026-07-27T06:00:00", 0)]   // Monday before fire time → today
    [InlineData(DayOfWeek.Monday, "2026-07-27T09:00:00", 7)]   // Monday after fire time → next week
    public void NextWeeklyDelay_FindsTheNextOccurrence(DayOfWeek day, string nowIso, int expectedDays)
    {
        // Arrange
        var now = DateTime.Parse(nowIso);

        // Act
        var delay = WeeklyDigestService.NextWeeklyDelay(day, new TimeSpan(7, 30, 0), now);

        // Assert
        (now + delay).Date.Should().Be(now.Date.AddDays(expectedDays));
        (now + delay).TimeOfDay.Should().Be(new TimeSpan(7, 30, 0));
    }
}
