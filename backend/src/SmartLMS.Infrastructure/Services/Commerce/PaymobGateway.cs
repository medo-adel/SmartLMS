using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;

namespace SmartLMS.Infrastructure.Services.Commerce;

/// <summary>
/// Paymob Accept adapter. Sources (checked 24 Nov 2026):
/// Create Intention https://developers.paymob.com/paymob-docs/developers/intention-apis/create-intention,
/// Unified Checkout https://developers.paymob.com/paymob-docs/developers/checkout-experiences/unified-checkout-redirection,
/// HMAC https://developers.paymob.com/paymob-docs/developers/webhook-callbacks-and-hmac/hmac/hmac-transaction-callback.
/// NOT yet run against Paymob's test environment: that needs the merchant's test keys.
/// </summary>
public sealed class PaymobGateway(GatewayOptions o, IHttpClientFactory http) : IPaymentGateway
{
    public PaymentMethod Method => PaymentMethod.Paymob;
    public bool Enabled => o.Enabled && !string.IsNullOrWhiteSpace(o.SecretKey) && !string.IsNullOrWhiteSpace(o.PublicKey) && !string.IsNullOrWhiteSpace(o.HmacSecret) && o.IntegrationId is not null;
    public GatewayCheckout CreateCheckout(Guid paymentId, decimal amount, string currency) => throw new InvalidOperationException("Paymob checkout is created through the Intention API.");

    public async Task<GatewayCheckout> CreateCheckoutAsync(Guid paymentId, decimal amount, string currency, BillingInfo b, CancellationToken ct)
    {
        var reference = $"PAYMOB-{paymentId:N}";
        var cents = (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        // Only data the user actually has is sent. Paymob documents phone_number as required; refuse early rather than invent one.
        if (string.IsNullOrWhiteSpace(b.Phone) || string.IsNullOrWhiteSpace(b.Email) || string.IsNullOrWhiteSpace(b.FullName))
            throw new AppException(400, "BILLING_INFO_REQUIRED", "Add your name, email and phone number to your profile before paying online.");
        var parts = b.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var billing = new Dictionary<string, string> { ["first_name"] = parts[0], ["email"] = b.Email!, ["phone_number"] = b.Phone! };
        if (parts.Length > 1) billing["last_name"] = parts[1];
        var body = new Dictionary<string, object?>
        {
            ["amount"] = cents, ["currency"] = currency.ToUpperInvariant(), ["payment_methods"] = new[] { o.IntegrationId!.Value },
            ["items"] = new[] { new { name = "SmartLMS order", amount = cents, description = "Order " + reference, quantity = 1 } },
            ["billing_data"] = billing,
            ["special_reference"] = reference, ["notification_url"] = o.NotificationUrl, ["redirection_url"] = o.RedirectionUrl,
        };
        var req = new HttpRequestMessage(HttpMethod.Post, o.BaseUrl.TrimEnd('/') + "/v1/intention/") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Token", o.SecretKey);
        HttpResponseMessage res;
        try { res = await http.CreateClient("paymob").SendAsync(req, ct); }
        catch (HttpRequestException) { throw new AppException(502, "GATEWAY_UNAVAILABLE", "The payment provider could not be reached."); }
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new AppException(502, "GATEWAY_REJECTED", "The payment provider rejected the payment request.");
        string? secret; try { secret = JsonDocument.Parse(text).RootElement.GetProperty("client_secret").GetString(); } catch (Exception e) when (e is JsonException or KeyNotFoundException) { secret = null; }
        if (string.IsNullOrWhiteSpace(secret)) throw new AppException(502, "GATEWAY_BAD_RESPONSE", "The payment provider returned an unexpected response.");
        var url = $"{o.BaseUrl.TrimEnd('/')}/unifiedcheckout/?publicKey={Uri.EscapeDataString(o.PublicKey!)}&clientSecret={Uri.EscapeDataString(secret)}";
        return new GatewayCheckout(reference, url);
    }

    public bool VerifySignature(ReadOnlySpan<byte> body, string? signature) => false;   // Paymob signs the transaction fields, not the raw body: see ReadCallback.

    /// <summary>Documented field order for the Transaction Processed callback.</summary>
    internal static readonly string[] HmacFields =
    [
        "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id", "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded",
        "is_standalone_payment", "is_voided", "order.id", "owner", "pending", "source_data.pan", "source_data.sub_type", "source_data.type", "success",
    ];

    public static string Concat(JsonElement obj)
    {
        var sb = new StringBuilder();
        foreach (var path in HmacFields)
        {
            var e = obj; foreach (var seg in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(seg, out e)) return ""; }
            sb.Append(e.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Null => "", JsonValueKind.String => e.GetString(), _ => e.GetRawText() });
        }
        return sb.ToString();
    }

    public GatewayCallback? ReadCallback(byte[] body, string? hmac)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(hmac)) return null;
        JsonElement obj; try { var root = JsonDocument.Parse(body).RootElement; if (!root.TryGetProperty("obj", out obj) || obj.ValueKind != JsonValueKind.Object) return null; } catch (JsonException) { return null; }
        var concat = Concat(obj); if (concat.Length == 0) return null;
        var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(o.HmacSecret!), Encoding.UTF8.GetBytes(concat));
        byte[] given; try { given = Convert.FromHexString(hmac.Trim()); } catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(given, expected)) return null;
        // Authentic. Correlate on order.merchant_order_id (our special_reference); amount_cents is in cents.
        string? reference = null;
        if (obj.TryGetProperty("order", out var order) && order.TryGetProperty("merchant_order_id", out var m) && m.ValueKind == JsonValueKind.String) reference = m.GetString();
        if (string.IsNullOrWhiteSpace(reference)) throw new ValidationAppException("The callback has no merchant order reference.");
        var cents = obj.GetProperty("amount_cents").GetDecimal();
        var ok = obj.GetProperty("success").ValueKind == JsonValueKind.True && obj.TryGetProperty("pending", out var pend) && pend.ValueKind == JsonValueKind.False
                 && obj.TryGetProperty("is_voided", out var v) && v.ValueKind == JsonValueKind.False && obj.TryGetProperty("is_refunded", out var rf) && rf.ValueKind == JsonValueKind.False
                 && obj.TryGetProperty("error_occured", out var er) && er.ValueKind == JsonValueKind.False;
        return new GatewayCallback(reference!, ok, cents / 100m, obj.GetProperty("currency").GetString() ?? "");
    }
}
