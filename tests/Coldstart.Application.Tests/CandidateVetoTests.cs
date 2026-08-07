using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class CandidateVetoTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Freshness = TimeSpan.FromDays(14);

    private static RawItem Item(string title, string author = "Acme BV", string text = "") =>
        new("funding", "id-" + title, title, text, author, "https://example.com/x", Now.ToString("O"));

    [Fact]
    public void Check_ItemMatchingAnExclusion_IsVetoedBeforeAnySpend()
    {
        // Arrange — the customer excludes recruiters
        var item = Item("Recruiters raise a round", text: "a recruitment agency");

        // Act
        var reason = CandidateVeto.Check(item, ["recruitment agency"], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().Be("excluded");
    }

    [Fact]
    public void Check_PublisherAsTheCompany_IsVetoed()
    {
        // Arrange — real observed junk: the feed's own publisher recorded as the prospect
        var item = Item("Some startup raises €10M", author: "news.crunchbase.com");

        // Act
        var reason = CandidateVeto.Check(item, [], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().Be("publisher-or-investor");
    }

    [Fact]
    public void Check_InvestorAsTheCompany_IsVetoed()
    {
        // Arrange — real observed junk: the VC, not the funded company
        var item = Item("Round led by the fund", author: "Lightspeed Venture Partners");

        // Act
        var reason = CandidateVeto.Check(item, [], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().Be("publisher-or-investor");
    }

    [Fact]
    public void Check_CompanyAlreadyHeldForThisCustomer_IsVetoedCaseInsensitively()
    {
        // Arrange — "Iceye" and "ICEYE" both appeared as separate leads in production
        var item = Item("ICEYE raises again", author: "ICEYE");
        var held = new HashSet<string>(["iceye"], StringComparer.OrdinalIgnoreCase);

        // Act
        var reason = CandidateVeto.Check(item, [], held, Now, Freshness);

        // Assert
        reason.Should().Be("duplicate-company");
    }

    [Fact]
    public void Check_ItemOlderThanTheFreshnessWindow_IsVetoed()
    {
        // Arrange
        var stale = new RawItem("funding", "id-old", "Old news", "", "Acme BV",
            "https://example.com/x", Now.AddDays(-30).ToString("O"));

        // Act
        var reason = CandidateVeto.Check(stale, [], new HashSet<string>(), Now, Freshness);

        // Assert — congratulating someone on month-old news is the failure this prevents
        reason.Should().Be("stale");
    }

    [Fact]
    public void Check_UnparseableDate_IsKeptRatherThanSilentlyDropped()
    {
        // Arrange — several sources emit no usable date at all
        var item = new RawItem("hackernews", "id-1", "Founder needs customers", "", "pg",
            "https://example.com/x", "not-a-date");

        // Act
        var reason = CandidateVeto.Check(item, [], new HashSet<string>(), Now, Freshness);

        // Assert — an absent date must not be treated as stale, or whole sources vanish
        reason.Should().BeNull();
    }

    [Fact]
    public void Check_GenuineProspect_IsKept()
    {
        // Arrange
        var item = Item("Acme BV raises seed round", text: "B2B software for logistics");

        // Act
        var reason = CandidateVeto.Check(item, ["recruitment agency"], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().BeNull();
    }
}
