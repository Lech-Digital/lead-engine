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

    // Structured targeting when the customer has filled it in, otherwise the legacy flat
    // fields, unchanged. In sub-project A the structured branch deliberately derives the
    // same inputs as the legacy one — sub-projects B (query generation) and C
    // (qualification) are what actually exploit the extra dimensions.
    //
    // Keywords deliberately never come from IndustryList/GeographyList: those are category
    // nouns ("B2B SaaS", "Netherlands"), not search-intent phrases. Sent as literal web-search
    // queries they return noise, and they dilute KeywordRotation's nightly paid-query budget
    // against SignalScorer's substring matching. Only the human-curated legacy TargetKeywords
    // ("struggling with outbound", "first sales hire") find real buying conversations.
    // Industries/geographies still reach the LLM via TriageCriteria.Describe — do not
    // reinstate the concat here.
    public static HarvestTargeting FromCustomer(CompanyProfile profile, Targeting targeting, string customerName)
    {
        if (targeting is null || !targeting.HasAny) return FromProfile(profile, customerName);

        var keywords = Split(profile.TargetKeywords);

        var signals = targeting.TriggerEventList.Count > 0
            ? targeting.TriggerEventList
            : Split(profile.BuyingSignals);

        var exclusions = targeting.ExclusionList.Count > 0
            ? targeting.ExclusionList
            : Split(profile.Disqualifiers);

        return new(
            profile.Offer ?? "",
            keywords,
            signals,
            exclusions,
            SenderName: string.IsNullOrWhiteSpace(profile.CompanyName) ? customerName : profile.CompanyName!,
            SenderTitle: "",
            MinScore: 0.4,
            MaxEnrichPerRun: 30);
    }

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrWhiteSpace(joined)
            ? []
            : joined.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
