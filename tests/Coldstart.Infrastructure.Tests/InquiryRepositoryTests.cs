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

    [Fact]
    public async Task AllAsync_ReturnsEveryInquiry_NewestFirst()
    {
        // Arrange
        var older = Inquiry.Create($"D-{Guid.NewGuid():N}", new EmailAddress("d@x.com"), "n", "s", null, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Inquiry.Create($"E-{Guid.NewGuid():N}", new EmailAddress("e@x.com"), "n", "s", null, DateTimeOffset.UtcNow);
        await using (var seed = fixture.NewDbContext())
        {
            seed.Inquiries.AddRange(older, newer);
            await seed.SaveChangesAsync();
        }

        // Act
        await using var read = fixture.NewDbContext();
        var all = (await new InquiryRepository(read).AllAsync(CancellationToken.None)).ToList();

        // Assert — both present, newest before older
        var idxNewer = all.FindIndex(i => i.Id == newer.Id);
        var idxOlder = all.FindIndex(i => i.Id == older.Id);
        idxNewer.Should().BeGreaterThanOrEqualTo(0);
        idxOlder.Should().BeGreaterThan(idxNewer);
    }

    [Fact]
    public async Task UpdateAsync_PersistsDismissedAt_AndRestoreClearsIt()
    {
        // Arrange
        var inquiry = Inquiry.Create($"F-{Guid.NewGuid():N}", new EmailAddress("f@x.com"), "n", "s", null, DateTimeOffset.UtcNow);
        await using (var seed = fixture.NewDbContext())
        {
            seed.Inquiries.Add(inquiry);
            await seed.SaveChangesAsync();
        }
        var when = DateTimeOffset.UtcNow;

        // Act — dismiss and persist
        await using (var write = fixture.NewDbContext())
        {
            var repo = new InquiryRepository(write);
            var toDismiss = await repo.GetAsync(inquiry.Id, CancellationToken.None);
            toDismiss!.Dismiss(when);
            await repo.UpdateAsync(toDismiss, CancellationToken.None);
        }

        // Assert — dismissed timestamp survived a round trip
        await using (var read = fixture.NewDbContext())
        {
            var found = await new InquiryRepository(read).GetAsync(inquiry.Id, CancellationToken.None);
            found!.DismissedAt.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
        }

        // Act — restore and persist; the row is never deleted, only its DismissedAt changes
        await using (var write2 = fixture.NewDbContext())
        {
            var repo = new InquiryRepository(write2);
            var toRestore = await repo.GetAsync(inquiry.Id, CancellationToken.None);
            toRestore!.Restore();
            await repo.UpdateAsync(toRestore, CancellationToken.None);
        }

        // Assert — still present, DismissedAt cleared
        await using (var read2 = fixture.NewDbContext())
        {
            var found = await new InquiryRepository(read2).GetAsync(inquiry.Id, CancellationToken.None);
            found.Should().NotBeNull();
            found!.DismissedAt.Should().BeNull();
        }
    }
}
