using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

public sealed record HarvestTargeting(
    string Offer,
    IReadOnlyList<string> TargetKeywords,
    IReadOnlyList<string> BuyingSignals,
    IReadOnlyList<string> Disqualifiers,
    string SenderName,
    string SenderTitle,
    double MinScore,
    int MaxEnrichPerRun)
{
    public bool HasKeywords => TargetKeywords.Count > 0 || BuyingSignals.Count > 0;

    public static HarvestTargeting FromNiche(NicheOptions n) => new(
        n.Offer, n.TargetKeywords, n.BuyingSignals, n.Disqualifiers,
        n.SenderName, n.SenderTitle, n.MinScore, n.MaxEnrichPerRun);

    public static HarvestTargeting FromProfile(CompanyProfile profile, string customerName) => new(
        profile.Offer ?? "",
        Split(profile.TargetKeywords),
        Split(profile.BuyingSignals),
        Split(profile.Disqualifiers),
        SenderName: string.IsNullOrWhiteSpace(profile.CompanyName) ? customerName : profile.CompanyName!,
        SenderTitle: "",
        MinScore: 0.4,
        MaxEnrichPerRun: 30);

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrWhiteSpace(joined)
            ? []
            : joined.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
