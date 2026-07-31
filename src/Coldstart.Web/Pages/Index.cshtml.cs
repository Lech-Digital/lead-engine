using Coldstart.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Coldstart.Web.Pages;

public class IndexModel(ColdstartDbContext db, IMemoryCache cache) : PageModel
{
    // Live engine ticker: 7-day totals, cached for an hour. (0, 0) hides the section.
    public (int Read, int Kept) EngineWeek { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        EngineWeek = await cache.GetOrCreateAsync("engine-ticker", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var since = DateTimeOffset.UtcNow.AddDays(-7);
            var rows = await db.HarvestRunStats.Where(s => s.RanAt >= since)
                .GroupBy(_ => 1)
                .Select(g => new { Read = g.Sum(s => s.Harvested), Kept = g.Sum(s => s.Saved) })
                .FirstOrDefaultAsync(ct);
            return (rows?.Read ?? 0, rows?.Kept ?? 0);
        });
    }

    // Outreach emails link here with ?company=<their name>#get-leads so the
    // sample-leads form arrives pre-filled — the prospect only fills the gaps.
    [BindProperty(SupportsGet = true, Name = "company")]
    public string? PrefillCompany { get; set; }
    [BindProperty(SupportsGet = true, Name = "name")]
    public string? PrefillName { get; set; }
    [BindProperty(SupportsGet = true, Name = "email")]
    public string? PrefillEmail { get; set; }
    [BindProperty(SupportsGet = true, Name = "niche")]
    public string? PrefillNiche { get; set; }
    [BindProperty(SupportsGet = true, Name = "sell")]
    public string? PrefillSell { get; set; }
}
