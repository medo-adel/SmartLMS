namespace SmartLMS.Infrastructure.Options;

public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    public string BaseUrl { get; set; } = "http://localhost:8001";

    /// <summary>Sent as X-AI-Service-Key. Supply through user-secrets or an environment variable, never source control.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;
}
