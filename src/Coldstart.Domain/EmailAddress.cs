namespace Coldstart.Domain;

public sealed record EmailAddress
{
    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            throw new DomainException($"Invalid email address: '{value}'.");
        Value = value.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}
