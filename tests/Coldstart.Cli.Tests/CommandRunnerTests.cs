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

    [Fact]
    public async Task RunAsync_Invite_ShouldPostBodyAndPrintMessage()
    {
        // Arrange
        var api = StubHandler.Client(req =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/admin/invites"
                ? (HttpStatusCode.Created, """{"customerId":"22222222-2222-2222-2222-222222222222","setPasswordLink":"https://x/reset","message":"Invitation sent to new@x.com."}""")
                : (HttpStatusCode.NotFound, ""), out var handler);
        var stdout = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(
            ["invite", "new@x.com", "--company", "New BV", "--niche", "HR SaaS"],
            api, stdout, new StringWriter());

        // Assert
        exit.Should().Be(0);
        stdout.ToString().Should().Contain("Invitation sent").And.Contain("https://x/reset");
        var sent = await handler.Requests.Single().Content!.ReadAsStringAsync();
        sent.Should().Contain("New BV").And.Contain("HR SaaS");
    }

    [Fact]
    public async Task RunAsync_InviteWithoutCompany_ShouldExit2WithoutCalling()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, "{}"), out var handler);
        var stderr = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["invite", "new@x.com"], api, new StringWriter(), stderr);

        // Assert
        exit.Should().Be(2);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_RemoveWithoutYes_ShouldRefuseAndExit2()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, "{}"), out var handler);
        var stderr = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["remove", "kim@acme.example"], api, new StringWriter(), stderr);

        // Assert
        exit.Should().Be(2);
        stderr.ToString().Should().Contain("--yes");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_HarvestByCompanyName_ShouldResolveIdThenPost()
    {
        // Arrange
        var api = StubHandler.Client(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/admin/customers" => (HttpStatusCode.OK, CustomersJson),
            "/api/admin/harvest/11111111-1111-1111-1111-111111111111" => (HttpStatusCode.Accepted, """{"enqueued":"Acme BV"}"""),
            _ => (HttpStatusCode.NotFound, ""),
        }, out var handler);
        var stdout = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["harvest", "acme bv"], api, stdout, new StringWriter());

        // Assert
        exit.Should().Be(0);
        handler.Requests.Should().HaveCount(2);
        stdout.ToString().Should().Contain("Acme BV");
    }

    [Fact]
    public async Task RunAsync_HarvestUnknownCompany_ShouldExit1()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, CustomersJson), out _);
        var stderr = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["harvest", "nope inc"], api, new StringWriter(), stderr);

        // Assert
        exit.Should().Be(1);
        stderr.ToString().Should().Contain("No customer named");
    }
}
