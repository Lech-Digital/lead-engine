using Coldstart.Application.Harvest;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class SuppressionTests
{
    [Fact]
    public void Create_NormalizesToLowercaseTrimmed()
    {
        // Arrange & Act
        var suppression = Suppression.Create("  Bluecore Energy  ", "objected 2026-07-31", DateTimeOffset.UtcNow);

        // Assert
        suppression.Value.Should().Be("bluecore energy");
        suppression.Note.Should().Be("objected 2026-07-31");
    }

    [Fact]
    public void Create_RejectsValuesShorterThanThreeChars()
    {
        // Arrange
        Action act = () => Suppression.Create(" bv ", null, DateTimeOffset.UtcNow);

        // Act & Assert — substring matching makes short values block half the market
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Bluecore Energy B.V.", true)]     // company name inside a longer legal name
    [InlineData("BLUECORE ENERGY", true)]          // case-insensitive
    [InlineData("Bluewater Shipping", false)]      // no match
    [InlineData("", false)]                        // empty text never matches
    public void IsMatch_ChecksNormalizedSubstring(string text, bool expected)
    {
        // Arrange
        string[] suppressed = ["bluecore energy"];

        // Act & Assert
        SuppressionMatcher.IsMatch(suppressed, text).Should().Be(expected);
    }

    [Fact]
    public void IsMatch_WithNoSuppressions_IsAlwaysFalse()
    {
        // Arrange & Act & Assert
        SuppressionMatcher.IsMatch([], "Anything At All").Should().BeFalse();
    }

    [Fact]
    public void IsMatch_ChecksEveryProvidedText()
    {
        // Arrange
        string[] suppressed = ["acme"];

        // Act & Assert — title clean, author matches
        SuppressionMatcher.IsMatch(suppressed, "Fresh funding round", "Acme Robotics").Should().BeTrue();
    }
}
