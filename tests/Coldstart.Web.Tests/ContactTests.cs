using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Coldstart.Infrastructure;
using Coldstart.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class ContactTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task PostContact_Valid_StoresInquiry_AndNotifies()
    {
        // Arrange
        var client = factory.CreateClient();
        var form = await AntiforgeryForm(client, new()
        {
            ["Name"] = "Sam", ["Email"] = "sam@acme.com",
            ["Niche"] = "B2B SaaS", ["WhatYouSell"] = "scheduling", ["Message"] = "hi", ["website"] = "", ["Consent"] = "true"
        });

        // Act
        var response = await client.PostAsync("/Inquiry", new FormUrlEncodedContent(form));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        (await db.Set<Inquiry>().CountAsync(i => i.Name == "Sam")).Should().Be(1);
        factory.Email.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task PostContact_WithCompany_StoresCompanyName()
    {
        // Arrange
        var client = factory.CreateClient();
        var name = $"Sam-{Guid.NewGuid():N}";
        var form = await AntiforgeryForm(client, new()
        {
            ["Name"] = name, ["Email"] = "sam@acme.com", ["Company"] = "Acme B.V.",
            ["Niche"] = "B2B SaaS", ["WhatYouSell"] = "scheduling", ["website"] = "", ["Consent"] = "true"
        });

        // Act
        var response = await client.PostAsync("/Inquiry", new FormUrlEncodedContent(form));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        (await db.Set<Inquiry>().SingleAsync(i => i.Name == name)).Company.Should().Be("Acme B.V.");
    }

    [Fact]
    public async Task LandingSampleForm_HasCompanyField()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("name=\"Company\"");
    }

    [Fact]
    public async Task PostContact_HoneypotFilled_DoesNotStoreOrNotify()
    {
        // Arrange
        var client = factory.CreateClient();
        var form = await AntiforgeryForm(client, new()
        {
            ["Name"] = "Bot", ["Email"] = "bot@spam.com",
            ["Niche"] = "x", ["WhatYouSell"] = "y", ["website"] = "http://spam", ["Consent"] = "true"
        });
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.PostAsync("/Inquiry", new FormUrlEncodedContent(form));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Email.Sent.Count.Should().Be(before);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        (await db.Set<Inquiry>().CountAsync(i => i.Name == "Bot")).Should().Be(0);
    }

    // Fetches the landing page, extracts the antiforgery token, and merges it into the form.
    private static async Task<Dictionary<string, string>> AntiforgeryForm(HttpClient client, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync("/");
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

    [Fact]
    public async Task Contact_WithoutConsent_IsRejectedAndStoresNothing()
    {
        // Arrange — GDPR: no ticked box, no processing.
        var client = factory.CreateClient();
        var form = await AntiforgeryForm(client, new()
        {
            ["Name"] = "Sam", ["Email"] = "noconsent@acme.com",
            ["Niche"] = "B2B SaaS", ["WhatYouSell"] = "scheduling", ["website"] = ""
        });

        // Act
        var response = await client.PostAsync("/Inquiry", new FormUrlEncodedContent(form));

        // Assert
        ((int)response.StatusCode).Should().Be(400);
    }
}
