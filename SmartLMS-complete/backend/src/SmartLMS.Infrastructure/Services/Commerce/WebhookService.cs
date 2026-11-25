using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Commerce;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";
    /// <summary>Off by default: a tenant admin must not be able to make the server call internal addresses. Turn on only in tests or a closed network.</summary>
    public bool AllowPrivateTargets { get; set; }
    public bool AllowHttp { get; set; }
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxAttempts { get; set; } = 5;
}
public sealed record WebhookRequest(string Url, List<string> EventTypes, bool? IsActive);
public sealed record ApiKeyRequest(string Name, List<string>? Scopes, DateTime? ExpiresAtUtc);

/// <summary>Outgoing webhooks: signed, retried with backoff, and logged. Secrets are encrypted at rest and shown once.</summary>
public sealed class WebhookService(ApplicationDbContext db, ICurrentUser me, IOptions<JwtOptions> jwt, IOptions<WebhookOptions> opts, IHttpClientFactory http, IJobQueue queue, AuditWriter audit)
{
    public static readonly string[] EventTypes = ["order.paid", "enrollment.created", "payment.manual.approved", "voucher.batch.ready", "*"];
    private static DateTime Now => DateTime.UtcNow;
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private byte[] Key => SHA256.HashData(Encoding.UTF8.GetBytes("webhook-secret-v1:" + jwt.Value.SigningKey));

    private string Protect(string secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var pt = Encoding.UTF8.GetBytes(secret); var ct = new byte[pt.Length]; var tag = new byte[16];
        using var g = new AesGcm(Key, 16); g.Encrypt(nonce, pt, ct, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. ct]);
    }
    private string Unprotect(string blob)
    {
        var all = Convert.FromBase64String(blob); var pt = new byte[all.Length - 28];
        using var g = new AesGcm(Key, 16); g.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), pt);
        return Encoding.UTF8.GetString(pt);
    }

    private async Task ValidateUrlAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.UserInfo.Length > 0 || url.Length > 500) throw new ValidationAppException("A valid URL without credentials is required.");
        if (u.Scheme != Uri.UriSchemeHttps && !(opts.Value.AllowHttp && u.Scheme == Uri.UriSchemeHttp)) throw new ValidationAppException("The URL must use https.");
        if (opts.Value.AllowPrivateTargets) return;
        IPAddress[] addrs; try { addrs = await Dns.GetHostAddressesAsync(u.DnsSafeHost, ct); } catch (SocketException) { throw new ValidationAppException("The host name could not be resolved."); }
        if (addrs.Length == 0 || addrs.Any(IsPrivate)) throw new ValidationAppException("The URL points to a private or internal address.");
    }
    public static bool IsPrivate(IPAddress a)
    {
        if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any) || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) return true;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (a.AddressFamily == AddressFamily.InterNetworkV6) return (a.GetAddressBytes()[0] & 0xFE) == 0xFC;   // fc00::/7
        var b = a.GetAddressBytes();
        return b[0] is 10 or 0 or 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127) || b[0] >= 224;
    }
    private static List<string> CleanTypes(List<string>? types)
    {
        var t = (types ?? []).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
        if (t.Count == 0 || t.Any(x => !EventTypes.Contains(x))) throw new ValidationAppException("EventTypes must be a non-empty list of: " + string.Join(", ", EventTypes));
        return t;
    }

    public async Task<object> ListAsync(CancellationToken ct) =>
        (await db.WebhookEndpoints.AsNoTracking().OrderByDescending(w => w.CreatedAtUtc).Take(100).ToListAsync(ct))
            .Select(w => new { w.Id, w.Url, EventTypes = JsonSerializer.Deserialize<List<string>>(w.EventTypesJson), w.IsActive, w.ConsecutiveFailures, w.CreatedAtUtc });

    public async Task<object> CreateAsync(WebhookRequest r, CancellationToken ct)
    {
        await ValidateUrlAsync(r.Url, ct); var types = CleanTypes(r.EventTypes);
        if (await db.WebhookEndpoints.CountAsync(ct) >= 20) throw new ConflictAppException("TOO_MANY", "At most 20 webhook endpoints per tenant.");
        var secret = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var w = new WebhookEndpoint { TenantId = Tid, Url = r.Url, SecretProtected = Protect(secret), EventTypesJson = JsonSerializer.Serialize(types), IsActive = r.IsActive ?? true, CreatedAtUtc = Now };
        db.WebhookEndpoints.Add(w); await db.SaveChangesAsync(ct);
        return new { w.Id, w.Url, EventTypes = types, w.IsActive, secret, note = "Store the secret now. It is not shown again." };
    }
    public async Task UpdateAsync(Guid id, WebhookRequest r, CancellationToken ct)
    {
        var w = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Webhook not found.");
        await ValidateUrlAsync(r.Url, ct); w.Url = r.Url; w.EventTypesJson = JsonSerializer.Serialize(CleanTypes(r.EventTypes));
        if (r.IsActive is { } a) { w.IsActive = a; if (a) w.ConsecutiveFailures = 0; }
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var w = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Webhook not found.");
        db.WebhookEndpoints.Remove(w); await db.SaveChangesAsync(ct);
    }
    public async Task<object> DeliveriesAsync(Guid id, PagedQueryLite q, CancellationToken ct)
    {
        if (!await db.WebhookEndpoints.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Webhook not found.");
        var query = db.WebhookDeliveries.AsNoTracking().Where(d => d.WebhookEndpointId == id);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(d => d.CreatedAtUtc).Skip(q.Skip).Take(q.Size).Select(d => new { d.Id, d.EventType, Status = d.Status.ToString(), d.AttemptCount, d.LastHttpStatus, d.LastError, d.NextAttemptAtUtc, d.DeliveredAtUtc, d.CreatedAtUtc }).ToListAsync(ct);
        return new { items, page = q.Page, pageSize = q.Size, totalCount = total };
    }

    /// <summary>Queues one delivery per matching endpoint. A delivery problem never fails the caller.</summary>
    public async Task EmitAsync(Guid tenantId, string eventType, object data, CancellationToken ct)
    {
        try
        {
            var endpoints = (await db.WebhookEndpoints.Where(w => w.IsActive).ToListAsync(ct)).Where(w => { var t = JsonSerializer.Deserialize<List<string>>(w.EventTypesJson) ?? []; return t.Contains("*") || t.Contains(eventType); }).ToList();
            if (endpoints.Count == 0) return;
            var ids = new List<Guid>();
            foreach (var w in endpoints)
            {
                var d = new WebhookDelivery { TenantId = tenantId, WebhookEndpointId = w.Id, EventType = eventType, Status = WebhookDeliveryStatus.Pending, NextAttemptAtUtc = Now, CreatedAtUtc = Now };
                d.PayloadJson = JsonSerializer.Serialize(new { id = d.Id, type = eventType, createdAtUtc = Now, data });
                db.WebhookDeliveries.Add(d); ids.Add(d.Id);
            }
            await db.SaveChangesAsync(ct);
            foreach (var id in ids)
            {
                var job = new BackgroundJob { TenantId = tenantId, Type = BackgroundJobType.WebhookDelivery, Status = BackgroundJobStatus.Pending, ReferenceType = "WebhookDelivery", ReferenceId = id, CreatedAtUtc = Now };
                db.BackgroundJobs.Add(job); await db.SaveChangesAsync(ct);
                var hf = queue.Enqueue(job.Id, tenantId);
                if (hf is not null) { job.HangfireJobId = hf; await db.SaveChangesAsync(ct); }
                else { db.BackgroundJobs.Remove(job); await db.SaveChangesAsync(ct); await DeliverAsync(id, ct); }   // background processing off: deliver now, with a short timeout
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }

    public async Task RetryAsync(Guid deliveryId, CancellationToken ct)
    {
        var d = await db.WebhookDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId, ct) ?? throw new NotFoundException("Delivery not found.");
        if (d.Status is not (WebhookDeliveryStatus.Failed or WebhookDeliveryStatus.Abandoned)) throw new ConflictAppException("NOT_RETRYABLE", "Only failed deliveries can be retried.");
        d.Status = WebhookDeliveryStatus.Pending; d.AttemptCount = 0; d.NextAttemptAtUtc = Now; await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Other, "WebhookDelivery", d.Id, new { action = "retry" }, ct);
        await DeliverAsync(d.Id, ct);
    }

    /// <summary>One attempt. Success marks it Delivered; failure schedules a retry with backoff, then Abandoned after MaxAttempts.</summary>
    public async Task DeliverAsync(Guid deliveryId, CancellationToken ct)
    {
        var d = await db.WebhookDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (d is null || d.Status != WebhookDeliveryStatus.Pending) return;
        var w = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == d.WebhookEndpointId, ct);
        if (w is null || !w.IsActive) { d.Status = WebhookDeliveryStatus.Abandoned; d.LastError = "Endpoint removed or disabled."; await db.SaveChangesAsync(ct); return; }
        int? status = null; string? error = null;
        try
        {
            await ValidateUrlAsync(w.Url, ct);   // checked again at send time: DNS can change after registration
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Unprotect(w.SecretProtected)), Encoding.UTF8.GetBytes(ts + "." + d.PayloadJson))).ToLowerInvariant();
            using var req = new HttpRequestMessage(HttpMethod.Post, w.Url) { Content = new StringContent(d.PayloadJson, Encoding.UTF8, "application/json") };
            req.Headers.Add("X-Webhook-Event", d.EventType); req.Headers.Add("X-Webhook-Id", d.Id.ToString()); req.Headers.Add("X-Webhook-Timestamp", ts); req.Headers.Add("X-Webhook-Signature", "sha256=" + sig);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(opts.Value.TimeoutSeconds));
            var client = http.CreateClient("webhooks"); client.Timeout = Timeout.InfiniteTimeSpan;
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            status = (int)res.StatusCode; if (!res.IsSuccessStatusCode) error = "HTTP " + status;
        }
        catch (ValidationAppException ex) { error = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException) { if (ct.IsCancellationRequested) throw; error = ex is HttpRequestException ? "Connection failed." : "Timed out."; }
        d.AttemptCount++; d.LastHttpStatus = status;
        if (error is null) { d.Status = WebhookDeliveryStatus.Delivered; d.DeliveredAtUtc = Now; d.LastError = null; d.NextAttemptAtUtc = null; w.ConsecutiveFailures = 0; }
        else
        {
            d.LastError = error; w.ConsecutiveFailures++;
            if (d.AttemptCount >= opts.Value.MaxAttempts) { d.Status = WebhookDeliveryStatus.Abandoned; d.NextAttemptAtUtc = null; }
            else { d.Status = WebhookDeliveryStatus.Pending; d.NextAttemptAtUtc = Now.AddMinutes(new[] { 1, 5, 30, 120, 720 }[Math.Min(d.AttemptCount - 1, 4)]); }
            if (w.ConsecutiveFailures >= 20) w.IsActive = false;   // stop hammering a dead endpoint; an admin re-enables it
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Recurring job: delivers pending rows whose retry time has come.</summary>
    public async Task DeliverDueAsync(CancellationToken ct)
    {
        var ids = await db.WebhookDeliveries.Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAtUtc <= Now).OrderBy(d => d.NextAttemptAtUtc).Select(d => d.Id).Take(50).ToListAsync(ct);
        foreach (var id in ids) await DeliverAsync(id, ct);
    }

    // ---- API keys ------------------------------------------------------------------------------------------------------
    public static readonly string[] Scopes = ["orders:read", "enrollments:read"];
    /// <summary>Validates an X-Api-Key for the tenant resolved from X-Tenant-Id (a key never works for another tenant) and the needed scope.</summary>
    public async Task AuthenticateKeyAsync(string? key, string scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("slk_", StringComparison.Ordinal)) throw new AppException(401, "UNAUTHORIZED", "A valid API key is required.");
        var h = HashKey(key.Trim());
        var k = await db.ApiKeys.FirstOrDefaultAsync(x => x.KeyHash == h, ct);
        if (k is null || k.RevokedAtUtc is not null || (k.ExpiresAtUtc is not null && k.ExpiresAtUtc <= Now)) throw new AppException(401, "UNAUTHORIZED", "A valid API key is required.");
        var scopes = JsonSerializer.Deserialize<List<string>>(k.ScopesJson) ?? [];
        if (!scopes.Contains(scope)) throw new ForbiddenAppException("INSUFFICIENT_SCOPE", "This API key does not have the required scope.");
        if (k.LastUsedAtUtc is null || k.LastUsedAtUtc < Now.AddMinutes(-1)) { k.LastUsedAtUtc = Now; await db.SaveChangesAsync(ct); }
    }

    public async Task<object> IntegrationOrdersAsync(PagedQueryLite q, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Where(o => o.Status == SmartLMS.Domain.Enums.OrderStatus.Paid).OrderByDescending(o => o.PaidAtUtc).Skip(q.Skip).Take(q.Size)
            .Select(o => new { o.Id, o.UserId, o.Total, o.Currency, o.PaidAtUtc, method = o.PaymentMethod.ToString() }).ToListAsync(ct);
    public async Task<object> IntegrationEnrollmentsAsync(PagedQueryLite q, CancellationToken ct) =>
        await db.Enrollments.AsNoTracking().OrderByDescending(e => e.EnrolledAtUtc).Skip(q.Skip).Take(q.Size)
            .Select(e => new { e.Id, e.UserId, e.CourseId, status = e.Status.ToString(), e.ProgressPercent, e.EnrolledAtUtc, e.ExpiresAtUtc }).ToListAsync(ct);

    public static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public async Task<object> ApiKeysAsync(CancellationToken ct) =>
        await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAtUtc).Take(200).Select(k => new { k.Id, k.Name, prefix = k.KeyPrefix, k.ScopesJson, k.ExpiresAtUtc, k.LastUsedAtUtc, k.RevokedAtUtc, k.CreatedAtUtc }).ToListAsync(ct);
    public async Task<object> CreateApiKeyAsync(ApiKeyRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100) throw new ValidationAppException("A name up to 100 characters is required.");
        var scopes = (r.Scopes ?? []).Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
        if (scopes.Count == 0 || scopes.Any(s => !Scopes.Contains(s))) throw new ValidationAppException("Scopes must be a non-empty list of: " + string.Join(", ", Scopes));
        if (r.ExpiresAtUtc is { } e && e <= Now) throw new ValidationAppException("The expiry must be in the future.");
        if (await db.ApiKeys.CountAsync(k => k.RevokedAtUtc == null, ct) >= 50) throw new ConflictAppException("TOO_MANY", "At most 50 active API keys per tenant.");
        var key = "slk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var k = new ApiKey { TenantId = Tid, Name = r.Name.Trim(), KeyPrefix = key[..12], KeyHash = HashKey(key), ScopesJson = JsonSerializer.Serialize(scopes), ExpiresAtUtc = r.ExpiresAtUtc, CreatedAtUtc = Now };
        db.ApiKeys.Add(k); await db.SaveChangesAsync(ct);
        return new { k.Id, k.Name, prefix = k.KeyPrefix, scopes, k.ExpiresAtUtc, key, note = "Store the key now. It is not shown again." };
    }
    public async Task RevokeApiKeyAsync(Guid id, CancellationToken ct)
    {
        var k = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("API key not found.");
        if (k.RevokedAtUtc is null) { k.RevokedAtUtc = Now; await db.SaveChangesAsync(ct); }
    }
}
