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

    [Fact]
    public async Task RecentAsync_ReturnsNewestFirst_UpToMax()
    {
        // Arrange — three inquiries with increasing CreatedAt
        var older = Inquiry.Create($"A-{Guid.NewGuid():N}", new EmailAddress("a@x.com"), "n", "s", null, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Inquiry.Create($"B-{Guid.NewGuid():N}", new EmailAddress("b@x.com"), "n", "s", null, DateTimeOffset.UtcNow);
        await using (var seed = fixture.NewDbContext())
        {
            seed.Inquiries.AddRange(older, newer);
            await seed.SaveChangesAsync();
        }

        // Act
        await using var read = fixture.NewDbContext();
        var recent = await new InquiryRepository(read).RecentAsync(50, CancellationToken.None);

        // Assert — includes both, newest before older
        var idxNewer = recent.ToList().FindIndex(i => i.Id == newer.Id);
        var idxOlder = recent.ToList().FindIndex(i => i.Id == older.Id);
        idxNewer.Should().BeGreaterThanOrEqualTo(0);
        idxOlder.Should().BeGreaterThan(idxNewer);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheInquiry_ById()
    {
        // Arrange
        var inquiry = Inquiry.Create($"C-{Guid.NewGuid():N}", new EmailAddress("c@x.com"), "niche", "sells", "msg", DateTimeOffset.UtcNow);
        await using (var seed = fixture.NewDbContext())
        {
            seed.Inquiries.Add(inquiry);
            await seed.SaveChangesAsync();
        }

        // Act
        await using var read = fixture.NewDbContext();
        var found = await new InquiryRepository(read).GetAsync(inquiry.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Niche.Should().Be("niche");
    }
}
