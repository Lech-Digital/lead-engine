namespace Coldstart.Infrastructure.Harvest;

public sealed class LlmOptions
{
    public string Provider { get; set; } = "groq";
    public string Model { get; set; } = "llama-3.3-70b-versatile";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
}
