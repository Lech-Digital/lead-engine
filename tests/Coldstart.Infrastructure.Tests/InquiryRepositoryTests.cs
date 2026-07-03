using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class InquiryRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddAsync_PersistsInquiry_Readable()
    {
        // Arrange
        await using var write = fixture.NewDbContext();
        var repo = new InquiryRepository(write);
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "B2B SaaS", "scheduling", "hi", DateTimeOffset.UtcNow);

        // Act
        await repo.AddAsync(inquiry, CancellationToken.None);

        // Assert
        await using var read = fixture.NewDbContext();
        var found = await read.Set<Inquiry>().SingleAsync(i => i.Id == inquiry.Id);
        found.Name.Should().Be("Sam");
        found.Email.Value.Should().Be("sam@acme.com");
    }
}
