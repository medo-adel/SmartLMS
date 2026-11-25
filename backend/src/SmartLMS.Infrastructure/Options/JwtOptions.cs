namespace SmartLMS.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "SmartLMS";
    public string Audience { get; set; } = "SmartLMS.Clients";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>At least 32 bytes. Supply through user-secrets or an environment variable (Jwt__SigningKey). Never committed; the app refuses to start without it.</summary>
    public string SigningKey { get; set; } = string.Empty;
}

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Development only. When true the log-only sender writes the code to the log. Never enable outside local development.</summary>
    public bool LogCodes { get; set; }
    public int ExpiryMinutes { get; set; } = 10;
    public int MaxAttempts { get; set; } = 5;
}
