namespace Coldstart.Domain;

// Who a customer sells to, structured so each pipeline stage can read the part it
// needs: query generation wants industries/geographies/triggers, the cheap gate wants
// geography/size/exclusions, the draft prompt wants the trigger for an honest "why now".
// Lists are stored newline-joined to match CompanyProfile's existing convention, which
// keeps the EF owned-type mapping free of value converters.
public sealed record Targeting(
    string? Industries,
    string? Geographies,
    string? BuyerRoles,
    int? SizeMin,
    int? SizeMax,
    string? TriggerEvents,
    string? Exclusions)
{
    public static Targeting Empty { get; } = new(null, null, null, null, null, null, null);

    public IReadOnlyList<string> IndustryList => Split(Industries);
    public IReadOnlyList<string> GeographyList => Split(Geographies);
    public IReadOnlyList<string> BuyerRoleList => Split(BuyerRoles);
    public IReadOnlyList<string> TriggerEventList => Split(TriggerEvents);
    public IReadOnlyList<string> ExclusionList => Split(Exclusions);

    // Empty targeting is a valid state: the harvest falls back to the legacy flat fields,
    // so a customer who never fills the form keeps working exactly as before.
    public bool HasAny =>
        IndustryList.Count > 0 || GeographyList.Count > 0 || BuyerRoleList.Count > 0
        || TriggerEventList.Count > 0 || ExclusionList.Count > 0
        || SizeMin is not null || SizeMax is not null;

    public static Targeting Create(
        IEnumerable<string> industries,
        IEnumerable<string> geographies,
        IEnumerable<string> buyerRoles,
        int? sizeMin,
        int? sizeMax,
        IEnumerable<string> triggerEvents,
        IEnumerable<string> exclusions)
    {
        if (sizeMin is < 0 || sizeMax is < 0)
            throw new DomainException("A company size cannot be negative.");
        if (sizeMin is { } min && sizeMax is { } max && min > max)
            throw new DomainException("The smallest company size must not be larger than the largest.");

        return new Targeting(Join(industries), Join(geographies), Join(buyerRoles),
            sizeMin, sizeMax, Join(triggerEvents), Join(exclusions));
    }

    private static string? Join(IEnumerable<string> values)
    {
        var cleaned = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return cleaned.Count == 0 ? null : string.Join('\n', cleaned);
    }

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrWhiteSpace(joined)
            ? []
            : joined.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
