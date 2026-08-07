using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class TargetingTests
{
    [Fact]
    public void Create_ListWithBlanksAndDuplicates_NormalisesToTrimmedDistinctEntries()
    {
        // Arrange
        string[] industries = ["  logistics ", "logistics", "", "   ", "3PL"];

        // Act
        var targeting = Targeting.Create(industries, [], [], null, null, [], []);

        // Assert
        targeting.IndustryList.Should().Equal("logistics", "3PL");
    }

    [Fact]
    public void Create_NoValuesAtAll_IsEmptyAndHasNothing()
    {
        // Act
        var targeting = Targeting.Create([], [], [], null, null, [], []);

        // Assert
        targeting.HasAny.Should().BeFalse();
        targeting.IndustryList.Should().BeEmpty();
    }

    [Fact]
    public void Create_AnySingleDimensionPopulated_HasAnyIsTrue()
    {
        // Act
        var targeting = Targeting.Create([], [], [], null, null, ["raised funding"], []);

        // Assert
        targeting.HasAny.Should().BeTrue();
        targeting.TriggerEventList.Should().Equal("raised funding");
    }

    [Fact]
    public void Create_SizeMinGreaterThanSizeMax_Throws()
    {
        // Act
        Action act = () => Targeting.Create([], [], [], sizeMin: 200, sizeMax: 10, [], []);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*smallest company size*");
    }

    [Fact]
    public void Create_NegativeSize_Throws()
    {
        // Act
        Action act = () => Targeting.Create([], [], [], sizeMin: -1, sizeMax: null, [], []);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public void Create_OnlyOneEndOfTheSizeBand_IsAllowed()
    {
        // Act
        var targeting = Targeting.Create([], [], [], sizeMin: 10, sizeMax: null, [], []);

        // Assert
        targeting.SizeMin.Should().Be(10);
        targeting.SizeMax.Should().BeNull();
        targeting.HasAny.Should().BeTrue();
    }

    [Fact]
    public void UpdateTargeting_OnCustomer_ReplacesTheStoredTargeting()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var targeting = Targeting.Create(["logistics"], ["NL"], ["Fleet Manager"], 10, 200, ["raised funding"], ["competitors"]);

        // Act
        customer.UpdateTargeting(targeting);

        // Assert
        customer.Targeting.Should().Be(targeting);
        customer.Targeting.GeographyList.Should().Equal("NL");
    }

    [Fact]
    public void Customer_Created_StartsWithEmptyTargeting()
    {
        // Act
        var customer = Customer.Create("Acme");

        // Assert
        customer.Targeting.Should().Be(Targeting.Empty);
        customer.Targeting.HasAny.Should().BeFalse();
    }

    [Fact]
    public void UpdateTargeting_WithNull_FallsBackToEmptyRatherThanNulling()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.UpdateTargeting(Targeting.Create(["logistics"], [], [], null, null, [], []));

        // Act
        customer.UpdateTargeting(null!);

        // Assert
        customer.Targeting.Should().Be(Targeting.Empty);
    }
}
