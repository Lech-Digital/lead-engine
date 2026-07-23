namespace Coldstart.Application.Customers;

public interface ICompanyBriefPolisher
{
    // Returns a tightened brief, or null if polishing is unavailable/failed (caller keeps the raw text).
    Task<string?> PolishAsync(string rawBrief, CancellationToken ct);
}
