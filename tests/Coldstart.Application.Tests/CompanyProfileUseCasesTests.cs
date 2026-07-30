using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class CompanyProfileUseCasesTests
{
    [Fact]
    public async Task Update_LoadsCustomer_AppliesProfile_Saves()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateCompanyProfile(repo);
        var input = new CompanyProfileInput("Acme Ltd", "1 Main St", "NL1", "KVK1", "B2B", "acme.com", "hi@acme.com");

        // Act
        await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        customer.Profile.CompanyName.Should().Be("Acme Ltd");
        await repo.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenCustomerMissing_DoesNothing()
    {
        // Arrange
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new UpdateCompanyProfile(repo);

        // Act
        var act = async () => await sut.UpdateAsync(Guid.NewGuid(), new CompanyProfileInput(null, null, null, null, null, null, null), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenProfileHasSearchFields_ShouldPreserveThem()
    {
        // Arrange — the AI-generated search profile (keywords etc.) is set at invite
        // time; a later profile edit from /app/profile must never wipe it, or every
        // subsequent harvest fails with "no keywords".
        var customer = Customer.Create("sam@acme.com");
        customer.UpdateProfile(new CompanyProfile(
            "Acme Ltd", null, null, null, "B2B SaaS", null, null,
            Offer: "outbound leads",
            TargetKeywords: "sales hire\nGTM",
            BuyingSignals: "raised funding",
            Disqualifiers: "competitors"));
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateCompanyProfile(repo);
        var input = new CompanyProfileInput("Acme Ltd", "1 Main St", null, null, "a new niche", null, null);

        // Act
        await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        customer.Profile.Niche.Should().Be("a new niche");
        customer.Profile.TargetKeywords.Should().Be("sales hire\nGTM");
        customer.Profile.BuyingSignals.Should().Be("raised funding");
        customer.Profile.Disqualifiers.Should().Be("competitors");
        customer.Profile.Offer.Should().Be("outbound leads");
    }

    [Fact]
    public async Task Get_MapsProfileToView_AndReportsEmpty()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GetCompanyProfile(repo);

        // Act
        var view = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        view.IsEmpty.Should().BeTrue();
        view.CompanyName.Should().BeNull();
    }

    [Fact]
    public async Task UpdateThenGet_CarriesBriefAndToggle()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var update = new UpdateCompanyProfile(repo);
        var get = new GetCompanyProfile(repo);
        var input = new CompanyProfileInput("Acme", null, null, null, null, null, null,
            Brief: "We help dentists fill their calendar.", MentionSourceTool: true);

        // Act
        await update.UpdateAsync(customer.Id.Value, input, CancellationToken.None);
        var view = await get.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        view.Brief.Should().Be("We help dentists fill their calendar.");
        view.MentionSourceTool.Should().BeTrue();
    }
}
