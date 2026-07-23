namespace Coldstart.Application.Harvest;

public sealed record SenderContext(string? Brief, bool MentionSourceTool)
{
    public static SenderContext None { get; } = new(null, false);
}
