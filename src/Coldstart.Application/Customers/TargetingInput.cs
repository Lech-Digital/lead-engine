namespace Coldstart.Application.Customers;

// Raw form text: one entry per line, exactly as typed. Normalising is the domain's job.
public sealed record TargetingInput(
    string? Industries,
    string? Geographies,
    string? BuyerRoles,
    int? SizeMin,
    int? SizeMax,
    string? TriggerEvents,
    string? Exclusions);

public sealed record TargetingResult(bool Succeeded, string? Message = null);
