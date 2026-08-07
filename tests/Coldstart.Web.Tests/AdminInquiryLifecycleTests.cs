using System.Text.RegularExpressions;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

// The admin Companies page (/admin) drives the whole inquiry lifecycle: Open (the operator's
// actual queue) / Converted (derived — a user account already exists for the email) /
// Dismissed (real, stored state) / All.
public class AdminInquiryLifecycleTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Converted_WhenAnAccountExistsForTheEmail_DisappearsFromOpen_AndShowsUnderConverted()
    {
        // Arrange — Inquiry.Email always normalizes to lowercase (EmailAddress), so the account is
        // deliberately created with an UPPERCASE version of the same address: matching still has
        // to succeed, proving the comparison is case-insensitive via Identity's own
        // NormalizedEmail rather than a hand-rolled one that happens to agree by coincidence.
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var storedEmail = $"lead-{Guid.NewGuid():N}@prospect.com";
        await SeedInquiryAsync(storedEmail, "Prospect Co");
        await TestAuth.CreateUserAsync(factory, storedEmail.ToUpperInvariant(), "Passw0rd!long");
        Guid customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            customerId = (await mgr.FindByEmailAsync(storedEmail))!.CustomerId!.Value;
        }

        // Act
        var openHtml = await client.GetStringAsync("/admin?filter=open");
        var convertedHtml = await client.GetStringAsync("/admin?filter=converted");

        // Assert — gone from Open, present under Converted with a link to its customer
        openHtml.Should().NotContain(storedEmail);
        convertedHtml.Should().Contain(storedEmail);
        RowContaining(convertedHtml, storedEmail).Should().Contain($"/admin/customers/{customerId}");
    }

    [Fact]
    public async Task Dismiss_ThenRestore_MovesTheInquiryBetweenOpenAndDismissed_WithoutDeletingIt()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var email = $"spam-{Guid.NewGuid():N}@prospect.com";
        var inquiryId = await SeedInquiryAsync(email, "Spammy Co");

        // Act — dismiss
        await TestAuth.PostFormAsync(client, "/admin?filter=open&handler=Dismiss", new() { ["id"] = inquiryId.ToString() });
        var openAfterDismiss = await client.GetStringAsync("/admin?filter=open");
        var dismissedAfterDismiss = await client.GetStringAsync("/admin?filter=dismissed");

        // Assert — moved from Open to Dismissed, offers Restore
        openAfterDismiss.Should().NotContain(email);
        dismissedAfterDismiss.Should().Contain(email);
        RowContaining(dismissedAfterDismiss, email).Should().Contain("Restore");

        // Assert — never deleted: still present in the DB and under "All"
        using (var scope = factory.Services.CreateScope())
        {
            var inquiries = scope.ServiceProvider.GetRequiredService<IInquiryRepository>();
            var stillThere = await inquiries.GetAsync(new InquiryId(inquiryId), CancellationToken.None);
            stillThere.Should().NotBeNull();
            stillThere!.DismissedAt.Should().NotBeNull();
        }
        var allAfterDismiss = await client.GetStringAsync("/admin?filter=all");
        allAfterDismiss.Should().Contain(email);

        // Act — restore
        await TestAuth.PostFormAsync(client, "/admin?filter=dismissed&handler=Restore", new() { ["id"] = inquiryId.ToString() });
        var openAfterRestore = await client.GetStringAsync("/admin?filter=open");
        var dismissedAfterRestore = await client.GetStringAsync("/admin?filter=dismissed");

        // Assert — back in Open, gone from Dismissed, still not deleted
        openAfterRestore.Should().Contain(email);
        RowContaining(openAfterRestore, email).Should().Contain("Dismiss");
        dismissedAfterRestore.Should().NotContain(email);
        using (var scope = factory.Services.CreateScope())
        {
            var inquiries = scope.ServiceProvider.GetRequiredService<IInquiryRepository>();
            var stillThere = await inquiries.GetAsync(new InquiryId(inquiryId), CancellationToken.None);
            stillThere.Should().NotBeNull();
            stillThere!.DismissedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task Filter_ShowsOnlyTheMatchingInquiries_ForEachState()
    {
        // Arrange — one open, one dismissed, one converted, all distinguishable by email
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);

        var openEmail = $"open-{Guid.NewGuid():N}@prospect.com";
        await SeedInquiryAsync(openEmail, "Open Co");

        var dismissedEmail = $"dismissed-{Guid.NewGuid():N}@prospect.com";
        var dismissedId = await SeedInquiryAsync(dismissedEmail, "Dismissed Co");
        await TestAuth.PostFormAsync(client, "/admin?handler=Dismiss", new() { ["id"] = dismissedId.ToString() });

        var convertedEmail = $"converted-{Guid.NewGuid():N}@prospect.com";
        await SeedInquiryAsync(convertedEmail, "Converted Co");
        await TestAuth.CreateUserAsync(factory, convertedEmail, "Passw0rd!long");

        // Act — scoped to the Inquiries card only: the unrelated "All companies" card below it
        // also names a company after the converted user's email (TestAuth.CreateUserAsync uses
        // the email as the company name), so scanning the whole page would false-positive there.
        var open = InquiriesSection(await client.GetStringAsync("/admin?filter=open"));
        var dismissed = InquiriesSection(await client.GetStringAsync("/admin?filter=dismissed"));
        var converted = InquiriesSection(await client.GetStringAsync("/admin?filter=converted"));
        var all = InquiriesSection(await client.GetStringAsync("/admin?filter=all"));

        // Assert — each tab shows exactly the inquiry that belongs there, and only that one
        open.Should().Contain(openEmail).And.NotContain(dismissedEmail).And.NotContain(convertedEmail);
        dismissed.Should().Contain(dismissedEmail).And.NotContain(openEmail).And.NotContain(convertedEmail);
        converted.Should().Contain(convertedEmail).And.NotContain(openEmail).And.NotContain(dismissedEmail);
        all.Should().Contain(openEmail).And.Contain(dismissedEmail).And.Contain(convertedEmail);
    }

    [Fact]
    public async Task Counts_ReflectHowManyInquiriesAreInEachState()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // A clean slate isn't guaranteed across the shared factory, so read the baseline counts first.
        var before = await client.GetStringAsync("/admin?filter=open");
        var (openBefore, convertedBefore, dismissedBefore, allBefore) = ReadCounts(before);

        var openEmail = $"cnt-open-{Guid.NewGuid():N}@prospect.com";
        await SeedInquiryAsync(openEmail, "Count Open Co");

        var dismissedEmail = $"cnt-dismissed-{Guid.NewGuid():N}@prospect.com";
        var dismissedId = await SeedInquiryAsync(dismissedEmail, "Count Dismissed Co");
        await TestAuth.PostFormAsync(client, "/admin?handler=Dismiss", new() { ["id"] = dismissedId.ToString() });

        var convertedEmail = $"cnt-converted-{Guid.NewGuid():N}@prospect.com";
        await SeedInquiryAsync(convertedEmail, "Count Converted Co");
        await TestAuth.CreateUserAsync(factory, convertedEmail, "Passw0rd!long");

        // Act
        var after = await client.GetStringAsync("/admin?filter=open");
        var (openAfter, convertedAfter, dismissedAfter, allAfter) = ReadCounts(after);

        // Assert — exactly one more in each bucket, and All is the sum of all three
        openAfter.Should().Be(openBefore + 1);
        dismissedAfter.Should().Be(dismissedBefore + 1);
        convertedAfter.Should().Be(convertedBefore + 1);
        allAfter.Should().Be(allBefore + 3);
        allAfter.Should().Be(openAfter + convertedAfter + dismissedAfter);
    }

    private static (int Open, int Converted, int Dismissed, int All) ReadCounts(string html)
    {
        int Read(string label)
        {
            var m = Regex.Match(html, $"{label} · (\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : throw new Xunit.Sdk.XunitException($"Count for \"{label}\" not found.");
        }
        return (Read("Open"), Read("Converted"), Read("Dismissed"), Read("All"));
    }

    private async Task<Guid> SeedInquiryAsync(string email, string company)
    {
        using var scope = factory.Services.CreateScope();
        var inquiries = scope.ServiceProvider.GetRequiredService<IInquiryRepository>();
        var inquiry = Inquiry.Create("Prospect", new EmailAddress(email), "niche", "sells", null, DateTimeOffset.UtcNow, company);
        await inquiries.AddAsync(inquiry, CancellationToken.None);
        return inquiry.Id.Value;
    }

    // Isolates the Inquiries card's markup from the rest of the page (notably "All companies",
    // which can coincidentally name a company after an inquiry's email).
    private static string InquiriesSection(string html)
    {
        var start = html.IndexOf("<h2>Inquiries</h2>", StringComparison.Ordinal);
        var end = html.IndexOf("<h2>All companies</h2>", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return html[start..end];
    }

    private static string RowContaining(string html, string needle)
    {
        foreach (Match row in Regex.Matches(html, "<tr\\b.*?</tr>", RegexOptions.Singleline))
        {
            if (row.Value.Contains(needle)) return row.Value;
        }
        throw new Xunit.Sdk.XunitException($"No <tr> found containing \"{needle}\".");
    }
}
