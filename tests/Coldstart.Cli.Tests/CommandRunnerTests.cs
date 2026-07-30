using System.Net;
using FluentAssertions;

namespace Coldstart.Cli.Tests;

public sealed class CommandRunnerTests
{
    private const string CustomersJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Acme BV","plan":"Full",
          "planRenewsAt":null,"hasStripeCustomer":true,"hasSubscription":true,
          "harvestStatus":"Done","lastHarvestAt":"2026-07-28T04:00:00+00:00",
          "leadsTotal":12,"leadsPendingReview":3,
          "users":[{"email":"kim@acme.example","role":"CompanyAdmin","status":"Active","inviteDaysLeft":null}]}]
        """;

    [Fact]
    public async Task RunAsync_Customers_ShouldRenderTableWithCompanyAndLeads()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, CustomersJson), out _);
        var stdout = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["customers"], api, stdout, new StringWriter());

        // Assert
        exit.Should().Be(0);
        stdout.ToString().Should().Contain("Acme BV").And.Contain("3/12").And.Contain("kim@acme.example");
    }

    [Fact]
    public async Task RunAsync_CustomersWithJsonFlag_ShouldPrintRawBody()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, CustomersJson), out _);
        var stdout = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["customers", "--json"], api, stdout, new StringWriter());

        // Assert
        exit.Should().Be(0);
        stdout.ToString().Should().Contain("\"leadsTotal\":12");
    }

    [Fact]
    public async Task RunAsync_WhenServerRejectsKey_ShouldPrintErrorAndExit1()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.Unauthorized, ""), out _);
        var stderr = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["stats"], api, new StringWriter(), stderr);

        // Assert
        exit.Should().Be(1);
        stderr.ToString().Should().Contain("401");
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ShouldPrintUsageAndExit2()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, "{}"), out var handler);
        var stderr = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["frobnicate"], api, new StringWriter(), stderr);

        // Assert
        exit.Should().Be(2);
        stderr.ToString().Should().Contain("Usage");
        handler.Requests.Should().BeEmpty();
    }
}
