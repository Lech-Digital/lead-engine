using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

// The narrowing from bare substring to word-boundary/suffix matching on investor markers
// is the actual deliverable here: this file is what stops anyone from silently widening
// the list back to a substring match without seeing what it costs. This denylist is
// GLOBAL across every customer, and a wrongly vetoed prospect is invisible — it never
// reaches a human, so there is no signal that anything went wrong.
public class PublisherDenylistTests
{
    [Theory]
    // Real observed production junk — must still be vetoed under the narrowed rule.
    [InlineData("White Star Capital")]
    [InlineData("Lightspeed Venture Partners")]
    [InlineData("Menlo Ventures")]
    [InlineData("news.crunchbase.com")]
    [InlineData("tech.eu")]
    public void IsPublisherOrInvestor_RealObservedOffender_IsVetoed(string author)
    {
        // Act
        var result = PublisherDenylist.IsPublisherOrInvestor(author);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    // Genuine prospects whose own name happens to contain an investor-form word mid-name
    // or fused into another word — these must NOT be vetoed by a global denylist.
    [InlineData("Human Capital Analytics")]
    [InlineData("SmartInvest")]
    [InlineData("Equity Payroll")]
    public void IsPublisherOrInvestor_GenuineProspectWithInvestorLikeWordMidName_IsKept(string author)
    {
        // Act
        var result = PublisherDenylist.IsPublisherOrInvestor(author);

        // Assert
        result.Should().BeFalse();
    }
}
