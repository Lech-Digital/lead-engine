using Coldstart.Application.Leads;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Areas.App.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadExportTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private static LeadSummary Summary(string company = "Acme", string draft = "Hi there", string subject = "Subj") => new(
        Guid.NewGuid(), company, "Yes", "Sent", "ceo@x.com", "Manual",
        subject, draft, "hackernews", 0.7, new DateTimeOffset(2026, 7, 30, 6, 58, 0, TimeSpan.Zero), "funding");

    [Fact]
    public void Build_QuotesCommasQuotesAndNewlines()
    {
        // Arrange
        var lead = Summary(company: "Acme, Inc \"NL\"", draft: "Line one\nLine two");

        // Act
        var csv = LeadCsv.Build([lead]);

        // Assert
        csv.Should().Contain("\"Acme, Inc \"\"NL\"\"\"");
        csv.Should().Contain("\"Line one\nLine two\"");
        csv.Should().StartWith("Company,Status,Fit,Score,Source,");
    }

    [Fact]
    public void Build_NeutralizesSpreadsheetFormulas()
    {
        // Arrange — harvested text is internet input; a leading '=' would execute in Excel
        var lead = Summary(company: "=HYPERLINK(\"https://evil\")");

        // Act
        var csv = LeadCsv.Build([lead]);

        // Assert
        csv.Should().Contain("'=HYPERLINK");
    }

    [Fact]
    public void Build_MapsPortalStatusLabels()
    {
        // Arrange
        var pending = Summary() with { Status = "PendingReview" };
        var shelved = Summary() with { Status = "Shelved" };

        // Act
        var csv = LeadCsv.Build([pending, shelved]);

        // Assert
        csv.Should().Contain("Pending review").And.Contain("Skipped").And.NotContain("Shelved");
    }

    [Fact]
    public async Task Export_ReturnsCsvDownload_WithAllLeads()
    {
        // Arrange — shared factory has an effectively unlimited free quota: everything exports
        var client = factory.CreateClient();
        var email = $"export-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        for (var i = 0; i < 3; i++) await AddLeadAsync(email, $"Export Co {i}", 0.9 - i * 0.1);

        // Act
        var response = await client.GetAsync("/app/history?handler=Export");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("coldstart-leads-");
        body.Should().Contain("Export Co 0").And.Contain("Export Co 2");
    }

    [Fact]
    public async Task Export_WhenSignedOut_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/app/history?handler=Export");

        // Assert
        ((int)response.StatusCode).Should().BeInRange(300, 399);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    private async Task AddLeadAsync(string email, string company, double score)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", "fp-" + Guid.NewGuid().ToString("N"), score, "outbound",
            DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }
}
