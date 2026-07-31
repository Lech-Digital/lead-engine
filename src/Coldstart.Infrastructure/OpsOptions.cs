namespace Coldstart.Infrastructure;

public sealed class OpsOptions
{
    // Operator alert address for harvest failures / enrichment (quota) trouble.
    public string AlertEmail { get; set; } = "andrzej@getcoldstart.nl";
}
