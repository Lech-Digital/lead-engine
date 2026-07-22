namespace Coldstart.Domain;

public sealed class LeadNote
{
    public Guid Id { get; private set; }
    public string Text { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private LeadNote() { }   // EF materialization

    internal LeadNote(Guid id, string text, DateTimeOffset when)
    {
        Id = id;
        Text = text;
        CreatedAt = when;
    }

    internal void Update(string text, DateTimeOffset when)
    {
        Text = text;
        UpdatedAt = when;
    }
}
