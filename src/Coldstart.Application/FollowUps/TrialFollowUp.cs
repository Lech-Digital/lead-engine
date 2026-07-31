namespace Coldstart.Application.FollowUps;

public static class TrialFollowUpStage
{
    public const int None = 0;
    public const int Day2 = 1;
    public const int Day5 = 2;
}

// Everything an outbound sender needs to compose one nudge.
public sealed record TrialFollowUp(Guid CustomerId, int Stage, string Company, int LockedLeadCount);

// Outbound port: deliver the nudge. Returns false when there is no deliverable
// address (the caller then leaves the stage unmarked so a later run can retry).
public interface ITrialFollowUpSender
{
    Task<bool> SendAsync(TrialFollowUp followUp, CancellationToken ct);
}

// Inbound port: evaluate all Free-plan customers and send whatever nudge is due.
public interface ISendTrialFollowUps
{
    Task<int> RunAsync(DateTimeOffset now, CancellationToken ct);
}
