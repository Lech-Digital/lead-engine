using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadDetailTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Detail_ForOwnedLead_RendersDraftAndContactLinks()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        var id = await AddLeadForUserAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        html.Should().Contain("Acme Co");
        html.Should().Contain("linkedin.com/search");
    }

    [Fact]
    public async Task Accept_PersistsApprovedStatus()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail2-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        var id = await AddLeadForUserAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new()
        {
            ["Input.Subject"] = "s", ["Input.Draft"] = "b", ["Input.Recipient"] = "ceo@acme.com",
        });

        // Act
        var post = await client.PostAsync($"/app/leads/{id}?handler=Accept", new FormUrlEncodedContent(form));

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        var accepted = await client.GetStringAsync("/app?tab=accepted");
        accepted.Should().Contain("Acme Co");
    }

    [Fact]
    public async Task Detail_ForAnotherCustomersLead_RedirectsToApp()
    {
        // Arrange — owner creates a lead
        var owner = factory.CreateClient();
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(owner, ownerEmail, "Passw0rd!");
        var id = await AddLeadForUserAsync(ownerEmail, "Owner Co", "fp-" + Guid.NewGuid().ToString("N"));

        // attacker is a different logged-in customer
        var attacker = factory.CreateClient(new() { AllowAutoRedirect = false });
        await AuthFlowTests.RegisterAsync(attacker, $"attacker-{Guid.NewGuid():N}@test.com", "Passw0rd!");

        // Act
        var response = await attacker.GetAsync($"/app/leads/{id}");

        // Assert — never leaks another tenant's lead
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/app");
    }

    private async Task<Guid> AddLeadForUserAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private static async Task<Dictionary<string, string>> AntiforgeryForm(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += marker.Length;
            var end = html.IndexOf('"', start);
            fields["__RequestVerificationToken"] = html[start..end];
        }
        return fields;
    }
}
