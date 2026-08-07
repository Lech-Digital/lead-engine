using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Web.Admin;

// "Converted" is derived, never stored: an inquiry counts as actioned once an account already
// exists with its email. Web layer on purpose, same as AdminUserService — it is ASP.NET-Identity
// -bound (NormalizedEmail, ILookupNormalizer) and must not sink into Application/Domain.
//
// One query for the whole page, not one per inquiry: BuildAsync fetches every account's
// NormalizedEmail + CustomerId once; matching against each inquiry then happens in memory using
// the same ILookupNormalizer Identity itself uses, so the comparison is exactly Identity's own
// case-insensitive semantics — not a hand-rolled ToUpper() that could drift from it.
internal sealed class InquiryConversionLookup(UserManager<ApplicationUser> users, ILookupNormalizer normalizer)
{
    public async Task<IReadOnlyDictionary<string, Guid?>> BuildAsync(CancellationToken ct)
    {
        var accounts = await users.Users
            .Where(u => u.NormalizedEmail != null)
            .Select(u => new { u.NormalizedEmail, u.CustomerId })
            .ToListAsync(ct);

        // Identity enforces unique emails, but be defensive about duplicates rather than throw.
        return accounts
            .GroupBy(a => a.NormalizedEmail!)
            .ToDictionary(g => g.Key, g => g.First().CustomerId);
    }

    public string Normalize(string email) => normalizer.NormalizeEmail(email) ?? email.ToUpperInvariant();
}
