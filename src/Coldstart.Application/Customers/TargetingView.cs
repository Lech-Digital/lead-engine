namespace Coldstart.Application.Customers;

// Newline-joined text ready to drop into a textarea. Empty string, never null, so the
// view never has to null-check.
public sealed record TargetingView(
    string Industries,
    string Geographies,
    string BuyerRoles,
    int? SizeMin,
    int? SizeMax,
    string TriggerEvents,
    string Exclusions);
