using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

// Pure, deterministic formatter turning a customer's structured Targeting plus their offer
// into the criteria block the triage prompt shows the model. One line per dimension;
// dimensions the customer never filled in are omitted entirely rather than printed as
// "none", so the prompt never implies a constraint that was never actually set.
public static class TriageCriteria
{
    public static string Describe(Targeting targeting, string offer)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(offer)) lines.Add($"Offer: {offer}");
        AddLine(lines, "Industries", targeting.IndustryList);
        AddLine(lines, "Geography", targeting.GeographyList);
        AddLine(lines, "Buyer roles", targeting.BuyerRoleList);
        var sizeBand = SizeBand(targeting.SizeMin, targeting.SizeMax);
        if (sizeBand is not null) lines.Add($"Company size: {sizeBand}");
        AddLine(lines, "Trigger events", targeting.TriggerEventList);
        AddLine(lines, "Exclusions", targeting.ExclusionList);
        return string.Join("\n", lines);
    }

    private static void AddLine(List<string> lines, string label, IReadOnlyList<string> values)
    {
        if (values.Count > 0) lines.Add($"{label}: {string.Join(", ", values)}");
    }

    // A range only when set — an unset bound must never fabricate a number in the prompt.
    private static string? SizeBand(int? min, int? max) => (min, max) switch
    {
        (null, null) => null,
        ({ } lo, { } hi) => $"{lo}-{hi} employees",
        ({ } lo, null) => $"{lo}+ employees",
        (null, { } hi) => $"up to {hi} employees",
    };
}
