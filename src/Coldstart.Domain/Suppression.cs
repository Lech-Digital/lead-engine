namespace Coldstart.Domain;

// A company (or domain) that objected to processing (GDPR art. 21) — the privacy policy's
// "one email and you're excluded" promise. Global: applies to every tenant's harvests.
// Matching is case-insensitive substring against harvested text and enriched company names.
public sealed class Suppression
{
    public Guid Id { get; private set; }
    public string Value { get; private set; } = string.Empty;   // normalized: trimmed, lowercase
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Suppression() { } // EF materialization

    public static Suppression Create(string value, string? note, DateTimeOffset createdAt)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        // Substring matching makes short values dangerous ("bv" would block half the Netherlands).
        if (normalized.Length < 3)
            throw new ArgumentException("A suppression needs at least 3 characters.", nameof(value));
        return new Suppression
        {
            Id = Guid.NewGuid(),
            Value = normalized,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = createdAt,
        };
    }
}
