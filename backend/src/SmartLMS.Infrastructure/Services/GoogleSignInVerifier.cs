using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Options;

namespace SmartLMS.Infrastructure.Services;

/// <summary>
/// Verifies a Google ID token against Google's own signing keys (fetched from the public OpenID configuration and
/// cached by the ConfigurationManager). The audience must be exactly the configured client id and the issuer Google.
/// </summary>
public sealed class GoogleSignInVerifier : IExternalSignInVerifier
{
    private readonly GoogleAuthOptions options;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> config;
    private readonly JsonWebTokenHandler handler = new();

    public GoogleSignInVerifier(IOptions<GoogleAuthOptions> options)
    {
        this.options = options.Value;
        config = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public string Provider => "google";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ClientId);
    public string? ClientId => IsConfigured ? options.ClientId : null;

    public async Task<ExternalIdentity> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (!IsConfigured) throw new AppException(501, "NOT_IMPLEMENTED", "Social login (google) is not configured in this build.");
        var discovery = await config.GetConfigurationAsync(ct);
        var result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuers = new[] { "accounts.google.com", "https://accounts.google.com" },
            ValidAudience = options.ClientId,
            IssuerSigningKeys = discovery.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        });
        if (!result.IsValid) throw new UnauthorizedAppException("INVALID_SOCIAL_TOKEN", "The Google sign-in token is not valid.");
        var claims = result.ClaimsIdentity;
        var sub = claims.FindFirst("sub")?.Value;
        var email = claims.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
        var verified = claims.FindFirst("email_verified")?.Value;
        var name = claims.FindFirst("name")?.Value;
        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email)) throw new UnauthorizedAppException("INVALID_SOCIAL_TOKEN", "The Google sign-in token is not valid.");
        return new ExternalIdentity("google", sub, email, string.IsNullOrWhiteSpace(name) ? email : name, verified == "true" || verified == "True");
    }

}
