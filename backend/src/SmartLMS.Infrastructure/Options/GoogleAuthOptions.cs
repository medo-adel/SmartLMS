namespace SmartLMS.Infrastructure.Options;

/// <summary>Google sign-in. With no ClientId the provider stays off: POST /auth/social/google answers 501 and the login page hides the button.</summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";
    /// <summary>OAuth client id from Google Cloud Console (Web application type). The same id must be configured in the app.</summary>
    public string ClientId { get; set; } = "";
}
