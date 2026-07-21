namespace Coldstart.Infrastructure.Harvest;

public sealed class LlmOptions
{
    public string Provider { get; set; } = "groq";
    public string Model { get; set; } = "llama-3.3-70b-versatile";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";

    // Minimum pause before every request after the first, so a sequential batch stays
    // under the free-tier rate limit (30 req/min) instead of slamming into 429s. 0 disables.
    public double RequestDelaySeconds { get; set; } = 2;
}
