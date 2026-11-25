using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartLMS.Infrastructure.Services.Commerce;

namespace SmartLMS.Tests;

/// <summary>Paymob adapter against the documented formats (developers.paymob.com HMAC transaction callback and Create Intention). A fake HTTP handler stands in for Paymob: nothing here talks to the real service.</summary>
public sealed class PaymobTests
{
    private const string Secret = "unit-hmac-secret";
    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }
    private sealed class Capture(string reply, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.Created) : HttpMessageHandler
    {
        public HttpRequestMessage? Req; public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Req = r; Body = await r.Content!.ReadAsStringAsync(ct); return new HttpResponseMessage(code) { Content = new StringContent(reply) }; }
    }
    private static GatewayOptions Opts() => new() { Enabled = true, PublicKey = "egy_pk_test_x", SecretKey = "egy_sk_test_x", HmacSecret = Secret, IntegrationId = 4569876, BaseUrl = "https://accept.paymob.com", NotificationUrl = "https://lms.example.test/api/v1/payments/gateways/paymob/webhook" };

    private const string Sample = """
    {"type":"TRANSACTION","obj":{"id":2556706,"pending":false,"amount_cents":100,"success":true,"is_auth":false,"is_capture":false,"is_standalone_payment":true,"is_voided":false,"is_refunded":false,"is_3d_secure":true,"integration_id":6741,"has_parent_transaction":false,"order":{"id":4778239,"merchant_order_id":"PAYMOB-REF1"},"created_at":"2020-03-25T18:39:44.719228","currency":"EGP","error_occured":false,"owner":4705,"source_data":{"pan":"2346","sub_type":"MasterCard","type":"card"}}}
    """;
    private static string Sign(string json) { var o = JsonDocument.Parse(json).RootElement.GetProperty("obj"); return Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(PaymobGateway.Concat(o)))).ToLowerInvariant(); }

    [Fact]
    public void Concatenation_matches_the_documented_worked_example()
        => Assert.Equal("1002020-03-25T18:39:44.719228EGPfalsefalse25567066741truefalsefalsefalsetruefalse47782394705false2346MasterCardcardtrue", PaymobGateway.Concat(JsonDocument.Parse(Sample).RootElement.GetProperty("obj")));

    [Fact]
    public void Callback_requires_a_valid_hmac_and_maps_amount_in_cents()
    {
        var gw = new PaymobGateway(Opts(), new Factory(new Capture("{}")));
        var body = Encoding.UTF8.GetBytes(Sample);
        var cb = gw.ReadCallback(body, Sign(Sample));
        Assert.NotNull(cb); Assert.True(cb!.Succeeded); Assert.Equal("PAYMOB-REF1", cb.Reference); Assert.Equal(1m, cb.Amount); Assert.Equal("EGP", cb.Currency);
        Assert.Null(gw.ReadCallback(body, null));
        Assert.Null(gw.ReadCallback(body, "zz"));
        Assert.Null(gw.ReadCallback(body, new string('0', 128)));
        Assert.Null(gw.ReadCallback(Encoding.UTF8.GetBytes(Sample.Replace("\"amount_cents\":100,", "\"amount_cents\":1,")), Sign(Sample)));   // amount tampered after signing
        var failed = Sample.Replace("\"success\":true", "\"success\":false");
        Assert.False(gw.ReadCallback(Encoding.UTF8.GetBytes(failed), Sign(failed))!.Succeeded);
    }

    [Fact]
    public async Task Create_intention_sends_the_documented_request_and_builds_the_unified_checkout_url()
    {
        var h = new Capture("""{"id":"x","client_secret":"egy_csk_test_abc","status":"intended"}""");
        var gw = new PaymobGateway(Opts(), new Factory(h));
        var id = Guid.NewGuid();
        var co = await gw.CreateCheckoutAsync(id, 123.45m, "egp", new BillingInfo("Mona Ali Hassan", "m@x.test", "+201001112222"), default);
        Assert.Equal("https://accept.paymob.com/v1/intention/", h.Req!.RequestUri!.ToString());
        Assert.Equal("Token", h.Req.Headers.Authorization!.Scheme); Assert.Equal("egy_sk_test_x", h.Req.Headers.Authorization.Parameter);
        var j = JsonDocument.Parse(h.Body!).RootElement;
        Assert.Equal(12345, j.GetProperty("amount").GetInt64()); Assert.Equal("EGP", j.GetProperty("currency").GetString());
        Assert.Equal(4569876, j.GetProperty("payment_methods")[0].GetInt64());
        Assert.Equal(co.Reference, j.GetProperty("special_reference").GetString());
        Assert.Equal("Mona", j.GetProperty("billing_data").GetProperty("first_name").GetString()); Assert.Equal("Ali Hassan", j.GetProperty("billing_data").GetProperty("last_name").GetString());
        Assert.Equal("+201001112222", j.GetProperty("billing_data").GetProperty("phone_number").GetString());
        Assert.False(j.GetProperty("billing_data").TryGetProperty("street", out _));   // nothing invented
        Assert.Equal("https://accept.paymob.com/unifiedcheckout/?publicKey=egy_pk_test_x&clientSecret=egy_csk_test_abc", co.Url);
    }

    [Fact]
    public async Task Provider_errors_become_a_clean_502_without_leaking_the_response()
    {
        var gw = new PaymobGateway(Opts(), new Factory(new Capture("""{"detail":"Integration ID/Name does not exist"}""", System.Net.HttpStatusCode.NotFound)));
        var ex = await Assert.ThrowsAsync<SmartLMS.Domain.Exceptions.AppException>(() => gw.CreateCheckoutAsync(Guid.NewGuid(), 10m, "EGP", new BillingInfo("A B", "a@x.test", "+201001112222"), default));
        Assert.Equal(502, ex.StatusCode); Assert.DoesNotContain("Integration ID", ex.Message);
    }

    [Fact]
    public async Task Missing_phone_or_email_is_refused_instead_of_invented()
    {
        var gw = new PaymobGateway(Opts(), new Factory(new Capture("{}")));
        var ex = await Assert.ThrowsAsync<SmartLMS.Domain.Exceptions.AppException>(() => gw.CreateCheckoutAsync(Guid.NewGuid(), 10m, "EGP", new BillingInfo("A B", "a@x.test", null), default));
        Assert.Equal("BILLING_INFO_REQUIRED", ex.Code);
    }

    [Fact]
    public void Gateway_is_disabled_until_every_key_is_configured()
    {
        var o = Opts(); o.HmacSecret = null;
        Assert.False(new PaymobGateway(o, new Factory(new Capture("{}"))).Enabled);
    }
}
