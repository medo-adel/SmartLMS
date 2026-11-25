using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartLMS.API.Infrastructure;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.API.Middleware;

/// <summary>
/// Decides which tenant a request runs in and writes it to ITenantContext before any query runs.
/// Sources, in order: the tenant in the access token (tid), the X-Tenant-Id header (Guid or slug), then the first host label (subdomain).
/// A token tenant that differs from the header is rejected. SuperAdmin tokens carry no tid: platform routes run in platform scope,
/// tenant routes need an active, unexpired SupportAccess membership.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IMemoryCache cache)
{
    public const string HeaderName = "X-Tenant-Id";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private sealed record Resolved(Guid Id, bool IsActive);

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenantContext, ICurrentUser user, ApplicationDbContext db, TimeProvider clock)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if ((!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)) || path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        var ct = ctx.RequestAborted;
        var header = ctx.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        Resolved? fromHeader = null;
        if (!string.IsNullOrEmpty(header))
        {
            fromHeader = await Lookup(db, header, ct);
            if (fromHeader is null)
            {
                await ErrorWriter.WriteAsync(ctx, 404, "TENANT_NOT_FOUND", "Unknown tenant.");
                return;
            }
        }

        if (user.IsAuthenticated)
        {
            if (user.TokenTenantId is { } tokenTenant)
            {
                if (fromHeader is not null && fromHeader.Id != tokenTenant)
                {
                    await ErrorWriter.WriteAsync(ctx, 403, "TENANT_MISMATCH", "The token belongs to a different tenant than X-Tenant-Id.");
                    return;
                }
                tenantContext.Set(tokenTenant, false);
                if (!await IsActive(db, tokenTenant, ct))
                {
                    await ErrorWriter.WriteAsync(ctx, 403, "TENANT_INACTIVE", "This tenant is not active.");
                    return;
                }
            }
            else if (IsPlatformRoute(path))
            {
                tenantContext.Set(null, true);
            }
            else
            {
                // SuperAdmin on a tenant route: only through an explicit, unexpired, unrevoked SupportAccess membership.
                if (fromHeader is null)
                {
                    await ErrorWriter.WriteAsync(ctx, 400, "TENANT_REQUIRED", "X-Tenant-Id is required for tenant routes.");
                    return;
                }
                var now = clock.GetUtcNow().UtcDateTime;
                var uid = user.UserId;
                var hasAccess = await db.UserTenantMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(m =>
                    m.TenantId == fromHeader.Id && m.UserId == uid && m.Kind == UserTenantMembershipKind.SupportAccess && m.RevokedAtUtc == null && (m.ExpiresAtUtc == null || m.ExpiresAtUtc > now), ct);
                if (!hasAccess)
                {
                    await ErrorWriter.WriteAsync(ctx, 403, "SUPPORT_ACCESS_REQUIRED", "SuperAdmin needs an active support-access grant for this tenant.");
                    return;
                }
                tenantContext.Set(fromHeader.Id, false, actingViaSupportAccess: true);
            }
        }
        else
        {
            var resolved = fromHeader ?? await FromSubdomain(ctx, db, ct);
            if (resolved is not null)
            {
                if (!resolved.IsActive)
                {
                    await ErrorWriter.WriteAsync(ctx, 403, "TENANT_INACTIVE", "This tenant is not active.");
                    return;
                }
                tenantContext.Set(resolved.Id, false);
            }
            else if (IsPlatformLogin(path) || path.Equals("/api/v1/auth/mail-delivery", StringComparison.OrdinalIgnoreCase))
            {
                // Platform-scope auth routes that need no tenant: login/refresh, and the mail-delivery mode probe.
                tenantContext.Set(null, true);
            }
            else if (ctx.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() is not null)
            {
                // Protected endpoint without a token: no tenant is set (queries return nothing) and authorization answers 401.
                tenantContext.Set(null, false);
            }
            else
            {
                await ErrorWriter.WriteAsync(ctx, 400, "TENANT_REQUIRED", "X-Tenant-Id (or a tenant subdomain) is required.");
                return;
            }
        }

        await next(ctx);
    }

    private static bool IsPlatformRoute(string path) =>
        (path.StartsWith("/api/v1/tenants", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/api/v1/tenants/current", StringComparison.OrdinalIgnoreCase))
        || path.StartsWith("/api/v1/me", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlatformLogin(string path) =>
        path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsActive(ApplicationDbContext db, Guid id, CancellationToken ct) =>
        (await Lookup(db, id.ToString(), ct))?.IsActive == true;

    private async Task<Resolved?> FromSubdomain(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        var host = ctx.Request.Host.Host;
        var parts = host.Split('.');
        if (parts.Length < 3 || System.Net.IPAddress.TryParse(host, out _)) return null;
        return await Lookup(db, parts[0], ct, bySubdomain: true);
    }

    private async Task<Resolved?> Lookup(ApplicationDbContext db, string key, CancellationToken ct, bool bySubdomain = false)
    {
        var cacheKey = $"tenant:{(bySubdomain ? "sub" : "key")}:{key.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out Resolved? hit)) return hit;
        var q = db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => !t.IsDeleted);
        var lowered = key.ToLowerInvariant();
        var found = bySubdomain
            ? await q.Where(t => t.Subdomain == lowered).Select(t => new Resolved(t.Id, t.IsActive)).FirstOrDefaultAsync(ct)
            : Guid.TryParse(key, out var gid)
                ? await q.Where(t => t.Id == gid).Select(t => new Resolved(t.Id, t.IsActive)).FirstOrDefaultAsync(ct)
                : await q.Where(t => t.Slug == lowered).Select(t => new Resolved(t.Id, t.IsActive)).FirstOrDefaultAsync(ct);
        // Only found tenants are cached, so a newly created tenant is reachable immediately. Deactivation takes up to 30 seconds to propagate.
        if (found is not null) cache.Set(cacheKey, found, CacheFor);
        return found;
    }
}
