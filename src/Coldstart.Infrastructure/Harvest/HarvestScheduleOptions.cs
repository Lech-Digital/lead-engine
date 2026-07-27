namespace Coldstart.Infrastructure.Harvest;

public sealed class HarvestScheduleOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan RunAtUtc { get; set; } = new(4, 0, 0);   // 04:00 UTC
    public int TopUpThreshold { get; set; } = 5;
}
