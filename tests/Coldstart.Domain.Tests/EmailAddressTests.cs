using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class EmailAddressTests
{
    [Fact]
    public void Ctor_WithValidAddress_NormalizesToLowerTrimmed()
    {
        // Arrange / Act
        var email = new EmailAddress("  Anand@Patronus.AI ");

        // Assert
        email.Value.Should().Be("anand@patronus.ai");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    public void Ctor_WithInvalidAddress_ThrowsDomainException(string bad)
    {
        // Arrange
        var act = () => new EmailAddress(bad);

        // Act / Assert
        act.Should().Throw<DomainException>();
    }
}
