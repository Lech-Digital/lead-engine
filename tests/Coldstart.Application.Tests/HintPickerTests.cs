using Coldstart.Application.Hints;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class HintPickerTests
{
    private static readonly DateOnly Day1 = new(2026, 7, 22);
    private static readonly DateOnly Day2 = new(2026, 7, 23);
    private static HintState Empty => new([], null);
    private static string FirstKey => OnboardingHints.All[0].Key;
    private static string SecondKey => OnboardingHints.All[1].Key;

    [Fact]
    public void All_HasStableOrderedKeys_StartingWithHowItWorks()
    {
        // Arrange + Act
        var keys = OnboardingHints.All.Select(h => h.Key).ToList();

        // Assert
        keys.Should().StartWith("how-it-works");
        keys.Should().OnlyHaveUniqueItems();
        OnboardingHints.All.Should().OnlyContain(h => !string.IsNullOrWhiteSpace(h.Body));
    }

    [Fact]
    public void Pick_OnFirstVisit_ShowsFirstHintAndStampsToday()
    {
        // Arrange
        var state = Empty;

        // Act
        var decision = HintPicker.Pick(state, Day1);

        // Assert
        decision.ToShow!.Key.Should().Be(FirstKey);
        decision.NextState.ShownOn.Should().Be(Day1);
        decision.NextState.SeenKeys.Should().BeEmpty();
    }

    [Fact]
    public void Pick_SameDayBeforeDismiss_KeepsShowingSameHint_WithoutAdvancing()
    {
        // Arrange — first hint was shown today, not yet dismissed
        var state = new HintState([], Day1);

        // Act
        var decision = HintPicker.Pick(state, Day1);

        // Assert
        decision.ToShow!.Key.Should().Be(FirstKey);
        decision.NextState.ShownOn.Should().Be(Day1);
    }

    [Fact]
    public void Pick_SameDayAfterDismiss_ShowsNothingMoreToday()
    {
        // Arrange — first hint shown AND dismissed today
        var state = new HintState([FirstKey], Day1);

        // Act
        var decision = HintPicker.Pick(state, Day1);

        // Assert
        decision.ToShow.Should().BeNull();
    }

    [Fact]
    public void Pick_NextDayAfterDismiss_ShowsSecondHint()
    {
        // Arrange — first dismissed, last shown was yesterday
        var state = new HintState([FirstKey], Day1);

        // Act
        var decision = HintPicker.Pick(state, Day2);

        // Assert
        decision.ToShow!.Key.Should().Be(SecondKey);
        decision.NextState.ShownOn.Should().Be(Day2);
    }

    [Fact]
    public void Pick_WhenAllSeen_ShowsNothing()
    {
        // Arrange
        var state = new HintState(OnboardingHints.All.Select(h => h.Key).ToList(), Day1);

        // Act
        var decision = HintPicker.Pick(state, Day2);

        // Assert
        decision.ToShow.Should().BeNull();
    }

    [Fact]
    public void Pick_IgnoresUnknownOrStaleSeenKeys()
    {
        // Arrange — a stale key no longer in the catalogue must not break selection
        var state = new HintState(["removed-old-key"], null);

        // Act
        var decision = HintPicker.Pick(state, Day1);

        // Assert
        decision.ToShow!.Key.Should().Be(FirstKey);
    }

    [Fact]
    public void Dismiss_AddsKeyToSeen_PreservingShownOn()
    {
        // Arrange
        var state = new HintState([], Day1);

        // Act
        var next = HintPicker.Dismiss(state, FirstKey);

        // Assert
        next.SeenKeys.Should().Contain(FirstKey);
        next.ShownOn.Should().Be(Day1);
    }

    [Fact]
    public void Reset_ClearsSeenAndShownOn()
    {
        // Arrange
        var state = new HintState([FirstKey, SecondKey], Day2);

        // Act
        var next = HintPicker.Reset();

        // Assert
        next.SeenKeys.Should().BeEmpty();
        next.ShownOn.Should().BeNull();
    }
}
