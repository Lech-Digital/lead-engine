using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class TargetingSettingsTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Targeting_Get_RendersTheFormWithTheRailItemActive()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"targeting-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app/settings/targeting");

        // Assert
        html.Should().Contain("class=\"rail-item active\" href=\"/app/settings/targeting\"");
        html.Should().Contain("Industries");
        html.Should().Contain("Trigger events");
    }

    [Fact]
    public async Task Targeting_Post_SavesAndShowsTheStoredValuesOnReload()
    {
        // Arrange — sign in, then post industries/geographies/triggers
        var client = factory.CreateClient();
        var email = $"targeting-save-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var form = await AntiforgeryForm(client, "/app/settings/targeting", new()
        {
            ["Input.Industries"] = "SaaS\nFintech",
            ["Input.Geographies"] = "Netherlands\nBelgium",
            ["Input.BuyerRoles"] = "CTO\nVP Engineering",
            ["Input.SizeMin"] = "10",
            ["Input.SizeMax"] = "250",
            ["Input.TriggerEvents"] = "Just raised funding",
            ["Input.Exclusions"] = "Existing customers",
        });

        // Act — POST the form, then GET the page again
        await client.PostAsync("/app/settings/targeting", new FormUrlEncodedContent(form));
        var html = await client.GetStringAsync("/app/settings/targeting");

        // Assert — the saved values come back in the textareas
        html.Should().Contain("SaaS");
        html.Should().Contain("Fintech");
        html.Should().Contain("Netherlands");
        html.Should().Contain("Belgium");
        html.Should().Contain("CTO");
        html.Should().Contain("VP Engineering");
        html.Should().Contain("value=\"10\"");
        html.Should().Contain("value=\"250\"");
        html.Should().Contain("Just raised funding");
        html.Should().Contain("Existing customers");
    }

    [Fact]
    public async Task Targeting_PostInvertedSizeBand_ShowsTheFieldErrorAndSavesNothing()
    {
        // Arrange — store a valid size band first
        var client = factory.CreateClient();
        var email = $"targeting-invert-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var goodForm = await AntiforgeryForm(client, "/app/settings/targeting", new()
        {
            ["Input.Industries"] = "SaaS", ["Input.Geographies"] = "", ["Input.BuyerRoles"] = "",
            ["Input.SizeMin"] = "10", ["Input.SizeMax"] = "50",
            ["Input.TriggerEvents"] = "", ["Input.Exclusions"] = "",
        });
        await client.PostAsync("/app/settings/targeting", new FormUrlEncodedContent(goodForm));

        var badForm = await AntiforgeryForm(client, "/app/settings/targeting", new()
        {
            ["Input.Industries"] = "SaaS", ["Input.Geographies"] = "", ["Input.BuyerRoles"] = "",
            ["Input.SizeMin"] = "999", ["Input.SizeMax"] = "1",
            ["Input.TriggerEvents"] = "", ["Input.Exclusions"] = "",
        });

        // Act
        var post = await client.PostAsync("/app/settings/targeting", new FormUrlEncodedContent(badForm));
        var postBody = await post.Content.ReadAsStringAsync();
        var reload = await client.GetStringAsync("/app/settings/targeting");

        // Assert — the response contains the "smallest company size" message and a reload
        // still shows the previous values
        postBody.Should().Contain("smallest company size");
        reload.Should().Contain("value=\"10\"");
        reload.Should().Contain("value=\"50\"");
        reload.Should().NotContain("value=\"999\"");
    }

    [Fact]
    public async Task Targeting_PostSuggest_WhenGenerationFails_LeavesStoredValuesIntact()
    {
        // Arrange — a stored profile, and a generator that fails (returns null)
        var client = factory.CreateClient();
        var email = $"targeting-suggest-fail-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var saveForm = await AntiforgeryForm(client, "/app/settings/targeting", new()
        {
            ["Input.Industries"] = "SaaS", ["Input.Geographies"] = "Netherlands", ["Input.BuyerRoles"] = "CTO",
            ["Input.SizeMin"] = "10", ["Input.SizeMax"] = "50",
            ["Input.TriggerEvents"] = "Funding", ["Input.Exclusions"] = "Competitors",
        });
        await client.PostAsync("/app/settings/targeting", new FormUrlEncodedContent(saveForm));
        factory.TargetingGenerator.Result = null;
        var suggestForm = await AntiforgeryForm(client, "/app/settings/targeting", new());

        // Act
        var suggest = await client.PostAsync("/app/settings/targeting?handler=Suggest", new FormUrlEncodedContent(suggestForm));
        var suggestBody = await suggest.Content.ReadAsStringAsync();
        var reload = await client.GetStringAsync("/app/settings/targeting");

        // Assert — the form is never blanked and nothing is persisted from a failed proposal;
        // the plain failure message is shown instead
        suggestBody.Should().Contain("suggest a targeting profile just now"); // HTML-encodes the apostrophe in "Couldn't"
        suggestBody.Should().Contain("SaaS");
        suggestBody.Should().Contain("Netherlands");
        suggestBody.Should().Contain("CTO");
        reload.Should().Contain("SaaS");
        reload.Should().Contain("Netherlands");
        reload.Should().Contain("CTO");
    }

    // GETs the given path, extracts the antiforgery token, and merges it into the form fields.
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
