using Coldstart.Web.Areas.App.Pages;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

// Portal copy for the engine's Fit verdict — plain language, not enum shouting.
// New user-visible copy introduced by the rail-polish redesign (Index/Detail pages).
public class FitLabelTests
{
    [Fact]
    public void Text_ForYes_ReturnsStrongFit()
    {
        // Arrange / Act
        var label = FitLabel.Text("Yes");

        // Assert
        label.Should().Be("Strong fit");
    }

    [Fact]
    public void Text_ForMaybe_ReturnsWorthALook()
    {
        // Arrange / Act
        var label = FitLabel.Text("Maybe");

        // Assert
        label.Should().Be("Worth a look");
    }

    [Fact]
    public void Text_ForUnknownValue_PassesItThroughUnchanged()
    {
        // Arrange — defensive default: an unexpected value renders itself rather than blanking
        var label = FitLabel.Text("No");

        // Assert
        label.Should().Be("No");
    }

    [Fact]
    public void PillClass_MapsYesAndMaybeToTheirOwnPillModifiers()
    {
        // Act / Assert
        FitLabel.PillClass("Yes").Should().Be("pz-pill-yes");
        FitLabel.PillClass("Maybe").Should().Be("pz-pill-maybe");
    }
}
