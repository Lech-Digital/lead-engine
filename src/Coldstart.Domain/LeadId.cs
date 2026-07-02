namespace Coldstart.Domain;

public sealed record LeadId(Guid Value)
{
    public static LeadId New() => new(Guid.NewGuid());
}
