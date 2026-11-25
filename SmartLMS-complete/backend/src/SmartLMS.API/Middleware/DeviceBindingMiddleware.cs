using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartLMS.API.Infrastructure;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.API.Middleware;

/// <summary>
/// Every authenticated request must prove it comes from the device the token was issued to: X-Device-Id (the client's device fingerprint)
/// is hashed and compared with the active UserDevices row named by the token's did claim. Checked against the database on every request,
/// so a device reset or a deactivated account takes effect immediately, not when the access token expires.
/// </summary>
public sealed class DeviceBindingMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Device-Id";

    public async Task InvokeAsync(HttpContext ctx, ICurrentUser user, ITokenService tokens, ApplicationDbContext db, TimeProvider clock)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        var isHub = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
        if (!user.IsAuthenticated || (!isHub && !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)))
        {
            await next(ctx);
            return;
        }

        var ct = ctx.RequestAborted;
        var fingerprint = ctx.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fingerprint) && isHub) fingerprint = ctx.Request.Query["device_id"].FirstOrDefault();   // browsers cannot set headers on a WebSocket
        if (string.IsNullOrWhiteSpace(fingerprint) || user.DeviceId is null || user.UserId is null)
        {
            await ErrorWriter.WriteAsync(ctx, 403, "DEVICE_REQUIRED", "X-Device-Id is required and must match the device you signed in on.");
            return;
        }

        var uid = user.UserId.Value;
        var did = user.DeviceId.Value;
        // The device row can have a null TenantId (platform users), so query filters would hide it. The predicate is bound to the token's own user and device ids.
        var row = await db.UserDevices.IgnoreQueryFilters().AsNoTracking().Where(d => d.Id == did && d.UserId == uid && d.IsActive)
            .Select(d => new { d.DeviceFingerprintHash, d.LastSeenAtUtc }).FirstOrDefaultAsync(ct);
        var presented = Encoding.UTF8.GetBytes(tokens.Hash(fingerprint));
        if (row is null || !CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(row.DeviceFingerprintHash)))
        {
            await ErrorWriter.WriteAsync(ctx, 403, "DEVICE_MISMATCH", "This device is not registered for this session.");
            return;
        }

        var active = await db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Id == uid && u.IsActive && !u.IsDeleted, ct);
        if (!active)
        {
            await ErrorWriter.WriteAsync(ctx, 401, "ACCOUNT_DISABLED", "This account is not active.");
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (now - row.LastSeenAtUtc > TimeSpan.FromMinutes(5))
        {
            // Telemetry only; bypasses the audit interceptor on purpose so it does not write an audit row per request.
            await db.UserDevices.IgnoreQueryFilters().Where(d => d.Id == did).ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAtUtc, now), ct);
        }

        await next(ctx);
    }
}
