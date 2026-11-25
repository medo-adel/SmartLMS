using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SmartLMS.Tests;

/// <summary>
/// Regression guard for the security audit: every endpoint the API maps either requires an authenticated caller
/// or is on a short, reviewed list of public routes. A new controller action without [Authorize] fails this test.
/// </summary>
public sealed class EndpointAuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public EndpointAuthorizationTests(ApiFactory f) => _f = f;

    // Each entry is public on purpose. See docs/SECURITY_AUDIT.md for why.
    private static readonly string[] PublicRoutes =
    [
        "api/v1/auth/register", "api/v1/auth/login", "api/v1/auth/refresh", "api/v1/auth/verify-email",
        "api/v1/auth/otp/request", "api/v1/auth/otp/verify", "api/v1/auth/forgot-password", "api/v1/auth/reset-password", "api/v1/auth/social/{provider}",
        "api/v1/auth/mail-delivery", // mode word only (mailbox/local-outbox/disabled), no addresses or config
        "api/v1/payments/gateways/{gateway}/webhook",
        "api/v1/bundles", "api/v1/subscription-plans", "api/v1/learning-paths", "api/v1/learning-paths/{id:guid}",
        "api/v1/courses", "api/v1/courses/{id:guid}", "api/v1/categories",
        "api/v1/certificates/verify/{code}",
        "api/v1/tenants/current/branding", "api/v1/tenants/current/logo",
        "api/v1/health",
        "api/v1/integration/orders", "api/v1/integration/enrollments", // authenticated inside the action by X-Api-Key and scope (WebhookService.AuthenticateKeyAsync)
    ];

    [Fact]
    public void Every_mapped_endpoint_requires_authentication_unless_it_is_on_the_reviewed_public_list()
    {
        var endpoints = _f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.True(endpoints.Count > 200, "expected the full API surface, found " + endpoints.Count);
        var open = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(e => e.RoutePattern.RawText!.TrimStart('/'))
            .Where(r => !r.StartsWith("swagger", StringComparison.OrdinalIgnoreCase))
            .Distinct().OrderBy(r => r).ToList();
        var unexpected = open.Except(PublicRoutes, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(unexpected.Count == 0, "Endpoints without authentication that are not on the public list: " + string.Join(", ", unexpected));
        var stale = PublicRoutes.Except(open, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(stale.Count == 0, "Public list entries that are no longer public or no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_hub_requires_authentication()
    {
        var hubs = _f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Where(e => e.RoutePattern.RawText!.StartsWith("/hubs/")).ToList();
        Assert.True(hubs.Count >= 3);
        Assert.All(hubs, h => Assert.NotEmpty(h.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }
}
