namespace Coldstart.Application.Leads;

public enum LeadActionStatus { Ok, NotFound, Invalid }

public sealed record LeadActionResult(LeadActionStatus Status, string? Message = null)
{
    public static readonly LeadActionResult Ok = new(LeadActionStatus.Ok);
    public static readonly LeadActionResult NotFound = new(LeadActionStatus.NotFound);
    public static LeadActionResult Invalid(string message) => new(LeadActionStatus.Invalid, message);
    public bool IsOk => Status == LeadActionStatus.Ok;
}
