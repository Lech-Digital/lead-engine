namespace Coldstart.Application.Harvest;

public sealed record SignalResult(double Score, IReadOnlyList<string> Matched);
