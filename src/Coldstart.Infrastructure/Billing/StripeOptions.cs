namespace Coldstart.Infrastructure.Billing;

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PriceMonthlyId { get; set; } = "";
    public string PriceSetupId { get; set; } = "";
    public bool IsConfigured => SecretKey.Length > 0 && PriceMonthlyId.Length > 0;
}
