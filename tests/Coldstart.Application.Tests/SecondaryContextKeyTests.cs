using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

// Pins SecondaryContextKey.For against the ACTUAL Author/Title shapes each ILeadSource
// implementation emits (read straight from the source files, not guessed):
//   - AdzunaSource:              Author = job.company.display_name (a real company name)
//                                 Title  = job title (NOT a company name)
//   - HackerNewsSource:          Author = HN "author" field (a lowercase username/handle)
//                                 Title  = the story's title
//   - FundingRssSource /
//     DutchBusinessNewsSource:   Author = the feed URL's Uri.Host (constant per feed,
//                                 e.g. every TechCrunch item shares "techcrunch.com")
//                                 Title  = the RSS <title> (names the company + the event)
//   - LangSearchSource:          Author = the hit's siteName (a publisher, not the company)
//                                 Title  = the hit's name/page title
//   - WebSearchSource:           Author = source/displayed_link (publisher name OR a bare
//                                 domain like "techcrunch.com", Scrapingdog can return either)
//                                 Title  = the organic result's title
public class SecondaryContextKeyTests
{
    private static RawItem Item(string title, string author) =>
        new("src", "1", title, "text", author, "https://x/1", "");

    [Fact]
    public void For_AdzunaShape_UsesTheRealCompanyNameAsAuthor()
    {
        // Arrange — AdzunaSource: Author = company.display_name, Title = job title
        var item = Item(title: "Senior Sales Engineer", author: "ASML");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert — Adzuna's Author is the one source that's already trustworthy
        key.Should().Be("ASML");
    }

    [Fact]
    public void For_AdzunaShapeWithMultiWordCompany_UsesAuthor()
    {
        // Arrange — a multi-word display_name, e.g. "Booking.com B.V." or "Coolblue B.V."
        var item = Item(title: "Warehouse Operative", author: "Coolblue B.V.");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert
        key.Should().Be("Coolblue B.V.");
    }

    [Fact]
    public void For_HackerNewsShape_RejectsTheLowercaseUsernameAndFallsBackToStoryTitle()
    {
        // Arrange — HackerNewsSource: Author = the HN "author" field, a lowercase handle
        var item = Item(title: "Acme (YC W24) is hiring a first AE", author: "pg");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert — "pg" names no company; the story title is the richer, usable field
        key.Should().Be("Acme (YC W24) is hiring a first AE");
    }

    [Fact]
    public void For_FundingRssShape_RejectsTheFeedHostAndFallsBackToTitle()
    {
        // Arrange — FundingRssSource: Author = label = Uri.Host of the feed URL, IDENTICAL
        // across every item from that feed (verified: `Uri.TryCreate(feedUrl, ...).Host`
        // always normalizes to lowercase) — this is the critical case: every TechCrunch
        // funding blurb shares this exact Author.
        var item = Item(title: "Acme raises $50M Series A led by Sequoia", author: "techcrunch.com");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert — the feed host names no company; the RSS title names the company AND the event
        key.Should().Be("Acme raises $50M Series A led by Sequoia");
    }

    [Fact]
    public void For_DutchBusinessNewsShape_RejectsTheFeedHostAndFallsBackToTitle()
    {
        // Arrange — DutchBusinessNewsSource subclasses FundingRssSource with the NL feed
        // set (Silicon Canals, Emerce, MT/Sprout) — same label-from-host shape
        var item = Item(title: "Partnify haalt €1,8 miljoen op", author: "siliconcanals.com");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert
        key.Should().Be("Partnify haalt €1,8 miljoen op");
    }

    [Fact]
    public void For_LangSearchShape_RejectsThePublisherStyledAsATitleCasedSiteName_UsesAuthor()
    {
        // Arrange — LangSearchSource: Author = hit.siteName, e.g. "Vestbee" — a real proper
        // noun (has an uppercase letter), so the heuristic (deliberately) can't distinguish
        // it from a genuine one-word company name and accepts it. Documented, not "fixed":
        // the brief's rule targets bare domains/handles, not every publisher name.
        var item = Item(title: "Dutch B2B SaaS firm Partnify secures €1.8M", author: "Vestbee");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert
        key.Should().Be("Vestbee");
    }

    [Fact]
    public void For_WebSearchShapeWithDomainDisplayedLink_RejectsItAndFallsBackToTitle()
    {
        // Arrange — WebSearchSource: Author = Str(hit,"source") ?? Str(hit,"displayed_link");
        // Scrapingdog can return a bare domain in displayed_link when "source" is absent
        var item = Item(title: "Acme raises $50M Series A", author: "techcrunch.com");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert
        key.Should().Be("Acme raises $50M Series A");
    }

    [Fact]
    public void For_WebSearchShapeWithBrandedSource_UsesAuthor()
    {
        // Arrange — WebSearchSource: Author = Str(hit,"source") when present, a branded name
        var item = Item(title: "Acme raises $50M Series A", author: "TechCrunch");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert — same caveat as the LangSearch case above: a title-cased publisher name
        // is indistinguishable from a real one-word company name under this heuristic
        key.Should().Be("TechCrunch");
    }

    [Fact]
    public void For_RealDottedCompanyName_IsNotMisclassifiedAsADomain()
    {
        // Arrange — a naive "contains a dot" domain check would wrongly reject a company
        // whose actual legal/brand name contains a dot; the uppercase letter saves it
        var item = Item(title: "Software Engineer", author: "Booking.com");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert
        key.Should().Be("Booking.com");
    }

    [Fact]
    public void For_BlankAuthorAndBlankTitle_ReturnsNull()
    {
        // Arrange — neither field yields anything usable
        var item = Item(title: "", author: "");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert
        key.Should().BeNull();
    }

    [Fact]
    public void For_BlankAuthorWithUsableTitle_FallsBackToTitle()
    {
        // Arrange — e.g. WebSearchSource can emit an empty site when neither JSON field is present
        var item = Item(title: "Acme raises $50M Series A", author: "  ");

        // Act
        var key = SecondaryContextKey.For(item);

        // Assert
        key.Should().Be("Acme raises $50M Series A");
    }
}
