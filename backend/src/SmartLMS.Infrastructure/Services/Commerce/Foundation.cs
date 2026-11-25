using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Commerce;

/// <summary>Phase 4 settings. Gateway secrets come from user-secrets or environment variables, never from the repository.</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";
    public int VoucherExportRetentionDays { get; set; } = 7;
    public int MaxVouchersPerBatch { get; set; } = 5000;
    public long MaxReceiptBytes { get; set; } = 5 * 1024 * 1024;
    public Dictionary<string, GatewayOptions> Gateways { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GatewayOptions
{
    public bool Enabled { get; set; }
    /// <summary>HMAC-SHA256 key shared with the provider's webhook sender.</summary>
    public string? Secret { get; set; }
    /// <summary>Hosted checkout page. {reference}, {amount} and {currency} are filled in.</summary>
    public string? CheckoutUrlTemplate { get; set; }
    // Paymob (Intention API + Unified Checkout). Secrets come from user-secrets or environment variables, never the repo.
    public string? PublicKey { get; set; }
    public string? SecretKey { get; set; }
    /// <summary>Dashboard, Settings, API Keys, HMAC Secret.</summary>
    public string? HmacSecret { get; set; }
    /// <summary>Online card integration id (Test or Live must match the keys).</summary>
    public long? IntegrationId { get; set; }
    public string BaseUrl { get; set; } = "https://accept.paymob.com";
    /// <summary>Public HTTPS URL of POST /api/v1/payments/gateways/paymob/webhook, sent as notification_url.</summary>
    public string? NotificationUrl { get; set; }
    public string? RedirectionUrl { get; set; }
}

public sealed record GatewayCheckout(string Reference, string Url);

/// <summary>
/// A payment provider adapter. This build creates the payment reference and hosted-checkout redirect from configuration and verifies the signed callback.
/// It does not call the provider's own API, so no merchant account, key or money is involved. A provider-specific adapter replaces <see cref="ConfiguredGateway"/>.
/// </summary>
public interface IPaymentGateway
{
    PaymentMethod Method { get; }
    bool Enabled { get; }
    GatewayCheckout CreateCheckout(Guid paymentId, decimal amount, string currency);
    Task<GatewayCheckout> CreateCheckoutAsync(Guid paymentId, decimal amount, string currency, BillingInfo billing, CancellationToken ct) => Task.FromResult(CreateCheckout(paymentId, amount, currency));
    bool VerifySignature(ReadOnlySpan<byte> body, string? signature);
    /// <summary>Verifies the callback and returns it in normalized form, or null when the signature is not valid.</summary>
    GatewayCallback? ReadCallback(byte[] body, string? signature);
}
public sealed record BillingInfo(string FullName, string? Email, string? Phone);
public sealed record GatewayCallback(string Reference, bool Succeeded, decimal Amount, string Currency);
internal sealed record NormalizedHook(string? Reference, string? Status, decimal? Amount, string? Currency);

public sealed class ConfiguredGateway(PaymentMethod method, GatewayOptions o) : IPaymentGateway
{
    public PaymentMethod Method => method;
    public bool Enabled => o.Enabled && !string.IsNullOrWhiteSpace(o.Secret) && !string.IsNullOrWhiteSpace(o.CheckoutUrlTemplate);
    public GatewayCheckout CreateCheckout(Guid paymentId, decimal amount, string currency)
    {
        var reference = $"{method.ToString().ToUpperInvariant()}-{paymentId:N}";
        var url = o.CheckoutUrlTemplate!.Replace("{reference}", Uri.EscapeDataString(reference)).Replace("{amount}", amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Replace("{currency}", Uri.EscapeDataString(currency));
        return new GatewayCheckout(reference, url);
    }
    public GatewayCallback? ReadCallback(byte[] body, string? signature) => ConfiguredGatewayJson.Read(this, body, signature);
    public bool VerifySignature(ReadOnlySpan<byte> body, string? signature)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(signature)) return false;
        var sig = signature.Trim(); if (sig.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) sig = sig[7..];
        byte[] given; try { given = Convert.FromHexString(sig); } catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(o.Secret!), body);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }
}

public static class ConfiguredGatewayJson
{
    public static GatewayCallback? Read(IPaymentGateway g, byte[] body, string? signature)
    {
        if (!g.Enabled || !g.VerifySignature(body, signature)) return null;
        NormalizedHook? b; try { b = System.Text.Json.JsonSerializer.Deserialize<NormalizedHook>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); } catch (System.Text.Json.JsonException) { throw new SmartLMS.Domain.Exceptions.ValidationAppException("The callback body is not valid JSON."); }
        if (b is null || string.IsNullOrWhiteSpace(b.Reference) || b.Amount is null) throw new SmartLMS.Domain.Exceptions.ValidationAppException("reference, status and amount are required.");
        var ok = string.Equals(b.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
        if (!ok && !string.Equals(b.Status, "failed", StringComparison.OrdinalIgnoreCase)) throw new SmartLMS.Domain.Exceptions.ValidationAppException("status must be succeeded or failed.");
        return new GatewayCallback(b.Reference!, ok, b.Amount.Value, b.Currency ?? "");
    }
}

public sealed class GatewayRegistry(IOptions<PaymentOptions> options, IHttpClientFactory http)
{
    public IPaymentGateway? Find(string name)
    {
        if (!Enum.TryParse<PaymentMethod>(name, true, out var m) || m is not (PaymentMethod.Paymob or PaymentMethod.Fawry or PaymentMethod.Stripe)) return null;
        options.Value.Gateways.TryGetValue(m.ToString(), out var o);
        if (m == PaymentMethod.Paymob) return new PaymobGateway(o ?? new GatewayOptions(), http);   // never the generic adapter: without every key it is visibly not configured
        return new ConfiguredGateway(m, o ?? new GatewayOptions());
    }
}

internal static class Tx
{
    /// <summary>Runs inside the caller's transaction when there is one, otherwise in its own. A failure rolls back everything and clears the change tracker.</summary>
    public static async Task<T> RunAsync<T>(ApplicationDbContext db, Func<Task<T>> work)
    {
        if (db.Database.CurrentTransaction is not null) return await work();
        await using var t = await db.Database.BeginTransactionAsync();
        try { var r = await work(); await t.CommitAsync(); return r; }
        catch { db.ChangeTracker.Clear(); throw; }
    }
    public static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Writes explicit audit rows for money and admin actions that are not simple entity edits.</summary>
public sealed class AuditWriter(ApplicationDbContext db, ICurrentUser me)
{
    public async Task WriteAsync(AuditLogAction action, string entity, object? id, object? values, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = db.CurrentTenantId, UserId = me.UserId, Action = action, EntityName = entity, EntityId = id?.ToString(),
            NewValuesJson = values is null ? null : System.Text.Json.JsonSerializer.Serialize(values), IpAddress = me.IpAddress, CorrelationId = me.CorrelationId, OccurredAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// The wallet ledger. Every change has an idempotency key, so a repeated webhook, retry or double click cannot credit or spend twice.
/// A debit is one conditional UPDATE (Balance >= amount), so two parallel purchases can never overdraw the wallet.
/// Call inside <see cref="Tx"/>: the balance change and the ledger row must commit together.
/// </summary>
public sealed class WalletLedger(ApplicationDbContext db)
{
    public async Task<Wallet> EnsureAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var w = await db.Wallets.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (w is not null) return w;
        w = new Wallet { TenantId = tenantId, UserId = userId };
        db.Wallets.Add(w);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(w).State = EntityState.Detached; w = await db.Wallets.FirstAsync(x => x.UserId == userId, ct); }
        return w;
    }

    /// <summary>Returns false when this key was already applied (nothing changed).</summary>
    public async Task<bool> ApplyAsync(Guid tenantId, Guid userId, WalletTransactionType type, decimal delta, string key, string? refType, Guid? refId, string? description, CancellationToken ct)
    {
        if (delta == 0) throw new ValidationAppException("The amount must not be zero.");
        delta = Tx.R(delta);
        var w = await EnsureAsync(tenantId, userId, ct);
        if (await db.WalletTransactions.AnyAsync(t => t.WalletId == w.Id && t.IdempotencyKey == key, ct)) return false;
        var rows = delta < 0
            ? await db.Wallets.Where(x => x.Id == w.Id && x.Balance >= -delta).ExecuteUpdateAsync(s => s.SetProperty(x => x.Balance, x => x.Balance + delta), ct)
            : await db.Wallets.Where(x => x.Id == w.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Balance, x => x.Balance + delta), ct);
        if (rows == 0) throw new AppException(402, "INSUFFICIENT_FUNDS", "The wallet balance is too low.");
        var after = await db.Wallets.AsNoTracking().Where(x => x.Id == w.Id).Select(x => x.Balance).FirstAsync(ct);
        db.WalletTransactions.Add(new WalletTransaction { TenantId = tenantId, WalletId = w.Id, Type = type, Amount = delta, BalanceAfter = after, ReferenceType = refType, ReferenceId = refId, Description = description, IdempotencyKey = key, CreatedAtUtc = DateTime.UtcNow });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new ConflictAppException("DUPLICATE_OPERATION", "This wallet operation is already being processed."); }
        return true;
    }
}
