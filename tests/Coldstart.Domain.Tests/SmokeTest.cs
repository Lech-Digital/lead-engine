using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_Compiles_AndTestsRun()
    {
        // Arrange / Act
        var truth = true;

        // Assert
        truth.Should().BeTrue();
    }
}
