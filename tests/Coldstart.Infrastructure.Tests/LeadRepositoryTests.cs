using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class LeadRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddAndReadBack_RoundTripsValueObjectsAndEnums()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var customer = Customer.Create("Coldstart");
        var lead = customer.AddLead("Patronus AI", Fit.Yes, "Subj", "Body", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress("anand@patronus.ai"));
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // Act
        await using var readDb = fixture.NewDbContext();
        var repo = new LeadRepository(readDb);
        var result = await repo.ForCustomerAsync(customer.Id, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Company.Should().Be("Patronus AI");
        result[0].Status.Should().Be(LeadStatus.Approved);
        result[0].RecipientEmail!.Value.Should().Be("anand@patronus.ai");
        result[0].EmailConfidence.Should().Be(EmailConfidence.None);
    }
}
