namespace Coldstart.Application.Customers;

public sealed record SearchProfileRequest(string Niche, string Info);

// Search fields stored on the customer's CompanyProfile. Keyword/signal/disqualifier
// strings are newline-joined; the harvest splits them.
public sealed record SearchProfile(string Offer, string TargetKeywords, string BuyingSignals, string Disqualifiers);

public interface ISearchProfileGenerator
{
    Task<SearchProfile?> GenerateAsync(SearchProfileRequest request, CancellationToken ct);
}
