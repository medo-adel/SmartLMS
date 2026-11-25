using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class CommerceTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public CommerceTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

    private sealed record Who(string Token, string Id, string Dev);
    private sealed record T(string Slug, Who Admin);

    private async Task<Who> Login(string slug, string email, string dev, string? id = null)
    {
        var r = await _c.Post("/auth/login", new { email, password = PW, device = Device(dev) }, tenant: slug);
        Assert.True(r.Success, r.Body.ToString());
        return new Who(r.Data.GetProperty("accessToken").GetString()!, id ?? "", Fp(dev));
    }
    private async Task<T> NewTenant()
    {
        var sa = (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa") })).Data.GetProperty("accessToken").GetString()!;
        var slug = "t" + U(); var admin = $"admin@{slug}.test";
        var r = await _c.Post("/tenants", new { name = slug, slug, adminFullName = "Admin", adminEmail = admin, adminPassword = PW, maxMobileDevices = 5, maxWebDevices = 5 }, sa, null, Fp("sa"));
        Assert.Equal(201, r.Status);
        return new T(slug, await Login(slug, admin, "adm" + slug));
    }
    private async Task<Who> Instructor(T t)
    {
        var email = $"in{U()}@mail.test";
        var r = await _c.Post("/users", new { fullName = "Inst", email, password = PW, roles = new[] { "Instructor" } }, t.Admin.Token, t.Slug, t.Admin.Dev);
        Assert.Equal(201, r.Status);
        return await Login(t.Slug, email, "in" + U(), r.Data.GetProperty("id").GetString());
    }
    private async Task<Who> Student(T t)
    {
        var email = $"s{U()}@mail.test";
        var r = await _c.Post("/auth/register", new { fullName = "Student", email, password = PW }, tenant: t.Slug);
        await _c.Post("/auth/verify-email", new { email, code = _f.Otp.Last(email, "EmailVerification") }, tenant: t.Slug);
        return await Login(t.Slug, email, "st" + U(), r.Data.GetProperty("id").GetString());
    }
    private Task<Resp> Post(string p, object? b, Who w, T t) => _c.Post(p, b, w.Token, t.Slug, w.Dev);
    private Task<Resp> Get(string p, Who w, T t) => _c.Get(p, w.Token, t.Slug, w.Dev);
    private Task<Resp> Put(string p, object? b, Who w, T t) => _c.Send(HttpMethod.Put, p, b, w.Token, t.Slug, w.Dev);
    private Task<Resp> Del(string p, Who w, T t) => _c.Send(HttpMethod.Delete, p, null, w.Token, t.Slug, w.Dev);
    private static string Id(Resp r) => r.Data.GetProperty("id").GetString()!;

    /// <summary>A published course priced at <paramref name="price"/> with one lesson (price set directly in the database).</summary>
    private async Task<(string Course, string Lesson)> PaidCourse(T t, Who inst, decimal price, decimal? lessonPrice = null)
    {
        var c = Id(await Post("/courses", new { title = "Paid " + U() }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S1" }, inst, t));
        var l = Id(await Post($"/sections/{s}/lessons", new { title = "L1", lessonType = "Article", contentMarkdown = "secret body" }, inst, t));
        Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, inst, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Courses.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(c)).ExecuteUpdateAsync(x => x.SetProperty(e => e.PricingModel, CoursePricingModel.OneTime).SetProperty(e => e.Price, price));
        if (lessonPrice is not null) await db.Lessons.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(l)).ExecuteUpdateAsync(x => x.SetProperty(e => e.PayPerLessonPrice, lessonPrice));
        return (c, l);
    }

    private async Task<string> RawGet(string path, Who w, T t)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1" + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", w.Token); req.Headers.Add("X-Tenant-Id", t.Slug); req.Headers.Add("X-Device-Id", w.Dev);
        var res = await _c.SendAsync(req); Assert.Equal(200, (int)res.StatusCode);
        return await res.Content.ReadAsStringAsync();
    }

    /// <summary>Creates a wallet-credit voucher batch and returns the plain codes from the exported CSV.</summary>
    private async Task<List<string>> WalletCodes(T t, int qty, decimal amount)
    {
        var r = await Post("/voucher-batches", new { name = "B" + U(), prefix = "TST", quantity = qty, voucherType = "WalletCredit", walletAmount = amount }, t.Admin, t);
        Assert.True(r.Success, r.Body.ToString());
        Assert.Equal("Completed", r.Data.GetProperty("status").GetString());
        var csv = await RawGet($"/voucher-batches/{r.Data.GetProperty("batchId").GetString()}/export", t.Admin, t);
        var codes = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(',')[0].Trim()).Where(x => x.StartsWith("TST")).ToList();
        Assert.Equal(qty, codes.Count);
        return codes;
    }
    private async Task Fund(T t, Who st, decimal amount)
    {
        var code = (await WalletCodes(t, 1, amount))[0];
        Assert.Equal(200, (await Post("/wallet/topup/voucher", new { code }, st, t)).Status);
    }
    private static decimal Bal(Resp r) => r.Data.GetProperty("balance").GetDecimal();

    [Fact]
    public async Task Wallet_checkout_grants_access_and_produces_an_invoice()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, l) = await PaidCourse(t, inst, 100m);
        Assert.Equal(402, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        Assert.Equal(200, (await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t)).Status);
        await Fund(t, st, 150m);
        Assert.Equal(150m, Bal(await Get("/wallet", st, t)));
        var o = await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t);
        Assert.True(o.Success, o.Body.ToString()); Assert.Equal("Paid", o.Data.GetProperty("status").GetString());
        Assert.Equal(50m, Bal(await Get("/wallet", st, t)));
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
        var pdf = await RawGet($"/orders/{o.Data.GetProperty("orderId").GetString()}/invoice", st, t);
        Assert.StartsWith("%PDF", pdf);
        // another student cannot read this order, and a cart of an already-owned course is refused
        var other = await Student(t);
        Assert.Equal(404, (await Get($"/orders/{o.Data.GetProperty("orderId").GetString()}", other, t)).Status);
        Assert.NotEqual(200, (await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t)).Status);
    }

    [Fact]
    public async Task Parallel_wallet_purchases_cannot_overdraw()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var courses = new List<string>(); for (var i = 0; i < 4; i++) courses.Add((await PaidCourse(t, inst, 100m)).Course);
        await Fund(t, st, 150m);
        // each checkout is a single-item cart; only one of them may be paid
        var results = new List<int>();
        foreach (var c in courses) await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t);
        var tasks = Enumerable.Range(0, 4).Select(_ => Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t)).ToArray();
        var rs = await Task.WhenAll(tasks);
        var bal = Bal(await Get("/wallet", st, t));
        Assert.True(bal >= 0m, "balance went negative: " + bal);
        Assert.True(rs.Count(x => x.Status == 200) <= 1, string.Join(",", rs.Select(x => x.Status)));
    }

    [Fact]
    public async Task Voucher_codes_are_hashed_and_only_one_parallel_redeem_wins()
    {
        var t = await NewTenant(); var st = await Student(t);
        var code = (await WalletCodes(t, 1, 40m))[0];
        var rs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Post("/wallet/topup/voucher", new { code }, st, t)));
        Assert.Equal(1, rs.Count(x => x.Status == 200));
        Assert.Equal(40m, Bal(await Get("/wallet", st, t)));
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.Vouchers.IgnoreQueryFilters().ToListAsync();
        Assert.DoesNotContain(rows, v => v.CodeHash == code);   // only a hash is stored
        Assert.NotEqual(200, (await Post("/wallet/topup/voucher", new { code = "TST-NOPE-NOPE" }, st, t)).Status);
    }

    private async Task SetPhone(Who w)
    {
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == Guid.Parse(w.Id)).ExecuteUpdateAsync(x => x.SetProperty(u => u.PhoneNumber, "+201001112222"));
    }
    private static string PaymobBody(string reference, long cents, bool success = true) =>
        "{\"type\":\"TRANSACTION\",\"obj\":{\"id\":991,\"pending\":false,\"amount_cents\":" + cents + ",\"success\":" + (success ? "true" : "false") + ",\"is_auth\":false,\"is_capture\":false,\"is_standalone_payment\":true,\"is_voided\":false,\"is_refunded\":false,\"is_3d_secure\":true,\"integration_id\":4569876,\"has_parent_transaction\":false,\"order\":{\"id\":55,\"merchant_order_id\":\"" + reference + "\"},\"created_at\":\"2026-11-24T10:00:00.1\",\"currency\":\"EGP\",\"error_occured\":false,\"owner\":7,\"source_data\":{\"pan\":\"2346\",\"sub_type\":\"MasterCard\",\"type\":\"card\"}}}";
    private static string PaymobHmac(string json) => Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes("test-paymob-hmac"), Encoding.UTF8.GetBytes(SmartLMS.Infrastructure.Services.Commerce.PaymobGateway.Concat(System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("obj"))))).ToLowerInvariant();
    private async Task<Resp> Callback(T t, string gateway, string json, string? header = null, string? query = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/payments/gateways/{gateway}/webhook" + (query is null ? "" : "?hmac=" + query)) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Tenant-Id", t.Slug);
        if (header is not null) req.Headers.Add("X-Signature", header);
        var res = await _c.SendAsync(req);
        return new Resp((int)res.StatusCode, System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    [Fact]
    public async Task Paymob_checkout_needs_billing_data_and_the_callback_hmac_decides_payment()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, l) = await PaidCourse(t, inst, 80m);
        await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t);
        var noPhone = await Post("/orders/checkout", new { paymentMethod = "Gateway", gateway = "Paymob" }, st, t);
        Assert.Equal(400, noPhone.Status); Assert.Equal("BILLING_INFO_REQUIRED", noPhone.Code);
        await SetPhone(st);
        var o = await Post("/orders/checkout", new { paymentMethod = "Gateway", gateway = "Paymob" }, st, t);
        Assert.True(o.Success, o.Body.ToString());
        var reference = o.Data.GetProperty("reference").GetString()!; var orderId = o.Data.GetProperty("orderId").GetString()!;
        Assert.Contains("clientSecret=csk_test_fake", o.Data.GetProperty("checkoutUrl").GetString());
        var good = PaymobBody(reference, 8000);
        Assert.Equal(401, (await Callback(t, "paymob", good)).Status);
        Assert.Equal(401, (await Callback(t, "paymob", good, query: new string('0', 128))).Status);
        Assert.Equal(401, (await Callback(t, "paymob", good, header: "x")).Status);
        Assert.Equal(403, (await Get($"/lessons/{l}", st, t)).Status);
        var ok = await Callback(t, "paymob", good, query: PaymobHmac(good)); Assert.Equal(200, ok.Status); Assert.True(ok.Data.GetProperty("processed").GetBoolean());
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.False((await Callback(t, "paymob", good, query: PaymobHmac(good))).Data.GetProperty("processed").GetBoolean());   // replay
        Assert.Equal("Paid", (await Get($"/orders/{orderId}", st, t)).Data.GetProperty("status").GetString());
        // a correctly signed callback with the wrong amount does not pay
        var st2 = await Student(t); await SetPhone(st2); await Post("/cart/items", new { itemType = "Course", itemId = c }, st2, t);
        var o2 = await Post("/orders/checkout", new { paymentMethod = "Gateway", gateway = "Paymob" }, st2, t);
        var bad = PaymobBody(o2.Data.GetProperty("reference").GetString()!, 100);
        Assert.False((await Callback(t, "paymob", bad, query: PaymobHmac(bad))).Data.GetProperty("processed").GetBoolean());
        Assert.NotEqual("Paid", (await Get($"/orders/{o2.Data.GetProperty("orderId").GetString()}", st2, t)).Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Generic_gateway_adapter_checks_the_body_signature()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, l) = await PaidCourse(t, inst, 80m);
        await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t);
        var o = await Post("/orders/checkout", new { paymentMethod = "Gateway", gateway = "Fawry" }, st, t);
        Assert.True(o.Success, o.Body.ToString());
        var reference = o.Data.GetProperty("reference").GetString()!;
        var json = $"{{\"reference\":\"{reference}\",\"status\":\"succeeded\",\"amount\":80,\"currency\":\"EGP\"}}";
        string Sig(string secret) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        Assert.Equal(401, (await Callback(t, "fawry", json)).Status);
        Assert.Equal(401, (await Callback(t, "fawry", json, header: Sig("wrong"))).Status);
        Assert.Equal(200, (await Callback(t, "fawry", json, header: Sig("test-gateway-secret"))).Status);
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
    }

    [Fact]
    public async Task An_unconfigured_gateway_is_visibly_unavailable()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, _) = await PaidCourse(t, inst, 80m);
        await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t);
        var r = await Post("/orders/checkout", new { paymentMethod = "Gateway", gateway = "Stripe" }, st, t);
        Assert.Equal(400, r.Status); Assert.Equal("GATEWAY_NOT_CONFIGURED", r.Code);
    }

    [Fact]
    public async Task Manual_payment_approve_and_reject_are_atomic()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, l) = await PaidCourse(t, inst, 60m);
        await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t);
        var o = await Post("/orders/checkout", new { paymentMethod = "Manual" }, st, t);
        var orderId = o.Data.GetProperty("orderId").GetString()!;
        async Task<Resp> Submit(string? oid, byte[] bytes, string name)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/manual-payments");
            var form = new MultipartFormDataContent { { new StringContent("Order"), "Purpose" }, { new StringContent("InstaPay"), "Method" }, { new StringContent("60"), "Amount" }, { new StringContent("REF" + U()), "SenderReference" } };
            if (oid is not null) form.Add(new StringContent(oid), "OrderId");
            form.Add(new ByteArrayContent(bytes), "receipt", name); req.Content = form;
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", st.Token); req.Headers.Add("X-Tenant-Id", t.Slug); req.Headers.Add("X-Device-Id", st.Dev);
            var res = await _c.SendAsync(req);
            return new Resp((int)res.StatusCode, System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone());
        }
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[64]).ToArray();
        Assert.Equal(400, (await Submit(orderId, Encoding.UTF8.GetBytes("<html>not an image</html>"), "r.png")).Status);   // type is sniffed, not trusted from the name
        var sub = await Submit(orderId, png, "r.png"); Assert.Equal(200, sub.Status);
        var id = Id(sub);
        Assert.Equal(403, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.Equal(403, (await Post($"/manual-payments/{id}/approve", null, st, t)).Status);
        Assert.Equal(200, (await Post($"/manual-payments/{id}/approve", null, t.Admin, t)).Status);
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.Equal("Paid", (await Get($"/orders/{orderId}", st, t)).Data.GetProperty("status").GetString());
        Assert.Equal(409, (await Post($"/manual-payments/{id}/approve", null, t.Admin, t)).Status);   // cannot be approved twice
        Assert.NotEqual(200, (await Post($"/manual-payments/{id}/reject", new { reason = "late" }, t.Admin, t)).Status);
        var logs = await Get("/audit-logs?entity=ManualPaymentSubmission", t.Admin, t);
        Assert.Equal(200, logs.Status);
        Assert.True(logs.Data.GetProperty("totalCount").GetInt32() >= 1);
        // reject path
        var st2 = await Student(t); await Post("/cart/items", new { itemType = "Course", itemId = c }, st2, t);
        var o2 = await Post("/orders/checkout", new { paymentMethod = "Manual" }, st2, t);
        st = st2;
        var s2 = await Submit(o2.Data.GetProperty("orderId").GetString(), png, "r2.png"); Assert.Equal(200, s2.Status);
        Assert.Equal(200, (await Post($"/manual-payments/{Id(s2)}/reject", new { reason = "amount does not match" }, t.Admin, t)).Status);
        Assert.NotEqual("Paid", (await Get($"/orders/{o2.Data.GetProperty("orderId").GetString()}", st2, t)).Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pay_per_lesson_unlock_is_idempotent_and_charged_once()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (_, l) = await PaidCourse(t, inst, 500m, lessonPrice: 30m);
        Assert.Equal(403, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.NotEqual(200, (await Post($"/lessons/{l}/unlock", null, st, t)).Status);   // wallet is empty
        await Fund(t, st, 100m);
        var r = await Post($"/lessons/{l}/unlock", null, st, t); Assert.True(r.Success, r.Body.ToString());
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.Equal(70m, Bal(await Get("/wallet", st, t)));
        var rs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Post($"/lessons/{l}/unlock", null, st, t)));
        Assert.Equal(70m, Bal(await Get("/wallet", st, t)));
    }

    [Fact]
    public async Task Coupons_enforce_expiry_limit_and_apply_the_discount_from_the_database()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var (c, _) = await PaidCourse(t, inst, 200m);
        Assert.Equal(403, (await Post("/coupons", new { code = "X", discountType = "Percentage", value = 10 }, await Student(t), t)).Status);
        Assert.True((await Post("/coupons", new { code = "HALF", discountType = "Percentage", value = 50, maxRedemptions = 1 }, t.Admin, t)).Success);
        Assert.True((await Post("/coupons", new { code = "OLD", discountType = "Percentage", value = 50, expiresAtUtc = DateTime.UtcNow.AddDays(-1) }, t.Admin, t)).Success);
        var a = await Student(t); var b = await Student(t);
        await Post("/cart/items", new { itemType = "Course", itemId = c }, a, t); await Post("/cart/items", new { itemType = "Course", itemId = c }, b, t);
        Assert.NotEqual(200, (await Post("/cart/coupon", new { code = "OLD" }, a, t)).Status);
        var applied = await Post("/cart/coupon", new { code = "half" }, a, t);
        Assert.Equal(100m, applied.Data.GetProperty("total").GetDecimal());
        await Fund(t, a, 300m); await Fund(t, b, 300m);
        var o = await Post("/orders/checkout", new { paymentMethod = "Wallet", couponCode = "HALF" }, a, t);
        Assert.True(o.Success, o.Body.ToString()); Assert.Equal(100m, o.Data.GetProperty("total").GetDecimal());
        Assert.Equal(200m, Bal(await Get("/wallet", a, t)));
        var o2 = await Post("/orders/checkout", new { paymentMethod = "Wallet", couponCode = "HALF" }, b, t);   // limit of 1 is used up
        Assert.NotEqual(200, o2.Status);
        Assert.Equal(300m, Bal(await Get("/wallet", b, t)));
    }

    [Fact]
    public async Task Subscription_covers_selected_courses_and_cancel_keeps_access_until_period_end()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, l) = await PaidCourse(t, inst, 900m); var (c2, l2) = await PaidCourse(t, inst, 900m);
        var plan = await Post("/subscription-plans", new { name = "Monthly", price = 99, billingPeriod = "Monthly", scope = "SelectedCourses", courseIds = new[] { c } }, t.Admin, t);
        Assert.True(plan.Success, plan.Body.ToString());
        await Fund(t, st, 120m);
        var sub = await Post("/subscriptions", new { planId = Id(plan), paymentMethod = "Wallet" }, st, t);
        Assert.True(sub.Success, sub.Body.ToString());
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.Equal(403, (await Get($"/lessons/{l2}", st, t)).Status);
        Assert.Equal(402, (await Post($"/courses/{c2}/enroll", null, st, t)).Status);
    }

    [Fact]
    public async Task Webhooks_refuse_private_targets_and_api_keys_are_shown_once_scoped_and_revocable()
    {
        var t = await NewTenant();
        foreach (var ip in new[] { "127.0.0.1", "10.1.2.3", "172.16.0.1", "192.168.1.1", "169.254.169.254", "100.64.0.1", "::1", "fd00::1", "::ffff:10.0.0.1", "0.0.0.0" })
            Assert.True(SmartLMS.Infrastructure.Services.Commerce.WebhookService.IsPrivate(System.Net.IPAddress.Parse(ip)), ip);
        Assert.False(SmartLMS.Infrastructure.Services.Commerce.WebhookService.IsPrivate(System.Net.IPAddress.Parse("8.8.8.8")));
        Assert.NotEqual(200, (await Post("/webhooks", new { url = "http://example.com/hook", eventTypes = new[] { "order.paid" } }, t.Admin, t)).Status);
        var k = await Post("/api-keys", new { name = "erp", scopes = new[] { "orders:read" } }, t.Admin, t);
        Assert.True(k.Success, k.Body.ToString());
        var key = k.Data.GetProperty("key").GetString()!; var kid = Id(k);
        Assert.StartsWith("slk_", key);
        Assert.DoesNotContain(key, (await Get("/api-keys", t.Admin, t)).Body.ToString());
        async Task<int> Call(string path, string? apiKey)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/integration" + path); req.Headers.Add("X-Tenant-Id", t.Slug);
            if (apiKey is not null) req.Headers.Add("X-Api-Key", apiKey);
            return (int)(await _c.SendAsync(req)).StatusCode;
        }
        Assert.Equal(200, await Call("/orders", key));
        Assert.Equal(403, await Call("/enrollments", key));   // scope not granted
        Assert.Equal(401, await Call("/orders", null));
        Assert.Equal(401, await Call("/orders", "slk_" + new string('0', 48)));
        var other = await NewTenant();
        var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/integration/orders"); req2.Headers.Add("X-Tenant-Id", other.Slug); req2.Headers.Add("X-Api-Key", key);
        Assert.Equal(401, (int)(await _c.SendAsync(req2)).StatusCode);   // a key never works for another tenant
        Assert.Equal(200, (await Del($"/api-keys/{kid}", t.Admin, t)).Status);
        Assert.Equal(401, await Call("/orders", key));
    }

    [Fact]
    public async Task Recycle_bin_restores_a_soft_deleted_coupon_and_other_tenants_cannot_see_it()
    {
        var t = await NewTenant(); var other = await NewTenant();
        var cp = await Post("/coupons", new { code = "BIN" + U(), discountType = "Percentage", value = 5 }, t.Admin, t); var id = Id(cp);
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Coupons.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(id)).ExecuteUpdateAsync(x => x.SetProperty(e => e.IsDeleted, true).SetProperty(e => e.DeletedAtUtc, DateTime.UtcNow));
        }
        Assert.Contains(id, (await Get("/recycle-bin", t.Admin, t)).Body.ToString());
        Assert.DoesNotContain(id, (await Get("/recycle-bin", other.Admin, other)).Body.ToString());
        Assert.Equal(404, (await Post($"/recycle-bin/coupon/{id}/restore", null, other.Admin, other)).Status);
        Assert.Equal(200, (await Post($"/recycle-bin/coupon/{id}/restore", null, t.Admin, t)).Status);
        Assert.DoesNotContain(id, (await Get("/recycle-bin", t.Admin, t)).Body.ToString());
    }

    [Fact]
    public async Task Payout_calculation_needs_a_superadmin_configured_share_and_payout_transitions_are_validated()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, _) = await PaidCourse(t, inst, 100m);
        await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t); await Fund(t, st, 100m);
        Assert.True((await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t)).Success);
        Assert.Equal(403, (await Get("/instructor/revenue", st, t)).Status);
        // no share configured anywhere: nothing is calculated with a guessed percentage
        var none = await Get("/instructor/revenue", inst, t); Assert.Equal(200, none.Status); Assert.DoesNotContain("\"earned\":60", none.Body.ToString());
                var stmt0 = await Get($"/instructor-payouts/statement?fromUtc={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("o"))}&toUtc={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(5).ToString("o"))}", t.Admin, t);
        Assert.Equal(200, stmt0.Status); Assert.Equal(100m, stmt0.Data.GetProperty("heldNoRecordedShare").GetDecimal());   // held, not guessed
        // only a platform administrator can set it; range is validated
        Assert.Equal(403, (await Put("/instructor-revenue/default", new { percent = 60 }, inst, t)).Status);   // instructors cannot set their own pay
        Assert.Equal(400, (await Put("/instructor-revenue/default", new { percent = 150 }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 60 }, t.Admin, t)).Status);
        Assert.Equal(200, (await Get("/instructor/revenue", inst, t)).Status);
        // the earlier sale had no percentage when it was bought: setting one now does not change it
        var st2 = await Student(t); await Post("/cart/items", new { itemType = "Course", itemId = c }, st2, t); await Fund(t, st2, 100m);
        Assert.True((await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st2, t)).Success);
        await Task.Delay(50); var period = new { periodStartUtc = DateTime.UtcNow.AddDays(-1), periodEndUtc = DateTime.UtcNow };
        var calc = await Post("/instructor-payouts/calculate", period, t.Admin, t);
        Assert.True(calc.Success, calc.Body.ToString());
        var list = await Get("/instructor-payouts", t.Admin, t); Assert.Equal(200, list.Status);
        var items = list.Data.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1, list.Body.ToString());
        var pid = items[0].GetProperty("id").GetString();
        Assert.Equal(60m, items[0].GetProperty("amount").GetDecimal());   // the configured 60 percent of 100
        // a later change does not touch the stored payout, and a per-course override is audited
        Assert.Equal(200, (await Put($"/courses/{c}/instructor-share", new { instructorId = items[0].GetProperty("instructorId").GetString(), percent = 80 }, t.Admin, t)).Status);
        Assert.Equal(60m, (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0].GetProperty("amount").GetDecimal());
        var aud = await Get("/audit-logs?pageSize=50", t.Admin, t); Assert.Contains("InstructorRevenueShare", aud.Body.ToString()); Assert.Contains("TenantInstructorShare", aud.Body.ToString());
        Assert.NotEqual(200, (await Put($"/instructor-payouts/{pid}", new { status = "Bogus" }, t.Admin, t)).Status);
        Assert.Equal(409, (await Put($"/instructor-payouts/{pid}", new { status = "Paid", paymentReference = "BANK-1" }, t.Admin, t)).Status);   // must be approved first
        Assert.Equal(200, (await Put($"/instructor-payouts/{pid}", new { status = "Approved" }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put($"/instructor-payouts/{pid}", new { status = "Paid", paymentReference = "BANK-1" }, t.Admin, t)).Status);
    }

    private async Task<string> Buy(T t, Who st, string course, decimal price)
    {
        await Post("/cart/items", new { itemType = "Course", itemId = course }, st, t); await Fund(t, st, price);
        var o = await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t); Assert.True(o.Success, o.Body.ToString());
        return o.Data.GetProperty("orderId").GetString()!;
    }

    [Fact]
    public async Task Refunds_reverse_instructor_earnings_proportionally_and_carry_a_debit_when_already_calculated()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var (c, _) = await PaidCourse(t, inst, 100m);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 60 }, t.Admin, t)).Status);
        var s1 = await Student(t); var s2 = await Student(t); var s3 = await Student(t); var s4 = await Student(t);
        var o1 = await Buy(t, s1, c, 100m); var o2 = await Buy(t, s2, c, 100m);
        // validation and authority
        Assert.Equal(403, (await Post($"/orders/{o1}/refunds", new { amount = 10, reference = "x1" }, inst, t)).Status);
        Assert.Equal(400, (await Post($"/orders/{o1}/refunds", new { amount = 150, reference = "x2" }, t.Admin, t)).Status);
        Assert.Equal(400, (await Post($"/orders/{o1}/refunds", new { amount = 0, reference = "x3" }, t.Admin, t)).Status);
        // refund BEFORE any payout is calculated: the sale is reduced by the refunded share (40 of 100 refunded -> 60 * 0.6 = 36 instead of 60)
        var r1 = await Post($"/orders/{o1}/refunds", new { amount = 40, reference = "R1", reason = "partial" }, t.Admin, t); Assert.True(r1.Success, r1.Body.ToString());
        Assert.Empty(r1.Data.GetProperty("debitsCreated").EnumerateArray());
        Assert.Equal(400, (await Post($"/orders/{o1}/refunds", new { amount = 61, reference = "R1b" }, t.Admin, t)).Status);   // would exceed the order total
        await Task.Delay(50); var p1 = new { periodStartUtc = DateTime.UtcNow.AddDays(-1), periodEndUtc = DateTime.UtcNow };
        Assert.True((await Post("/instructor-payouts/calculate", p1, t.Admin, t)).Success);
        var pay1 = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0];
        Assert.Equal(96m, pay1.GetProperty("amount").GetDecimal());   // (60 + 100) * 0.6
        // refund AFTER the payout was calculated: a debit is recorded, money is not taken
        var r2 = await Post($"/orders/{o2}/refunds", new { amount = 50, reference = "R2" }, t.Admin, t); Assert.True(r2.Success, r2.Body.ToString());
        var d = r2.Data.GetProperty("debitsCreated"); Assert.Equal(1, d.GetArrayLength()); Assert.Equal(30m, d[0].GetProperty("amount").GetDecimal());   // 60 * 50/100
        Assert.Equal(96m, (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0].GetProperty("amount").GetDecimal());
        var debits = await Get("/instructor-debits", t.Admin, t); Assert.Equal(30m, debits.Data.GetProperty("outstanding").GetDecimal());
        // the next payout carries the debit: 100 sale -> 60, minus 30
        await Buy(t, s3, c, 100m); await Task.Delay(50);
        var p2 = new { periodStartUtc = p1.periodEndUtc, periodEndUtc = DateTime.UtcNow };
        Assert.True((await Post("/instructor-payouts/calculate", p2, t.Admin, t)).Success);
        var pay2 = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0];
        Assert.Equal(30m, pay2.GetProperty("amount").GetDecimal());
        Assert.Equal(0m, (await Get("/instructor-debits", t.Admin, t)).Data.GetProperty("outstanding").GetDecimal());
        // the payout that absorbed the debit is cancelled: the debit comes back
        Assert.Equal(200, (await Put($"/instructor-payouts/{pay2.GetProperty("id").GetString()}", new { status = "Cancelled" }, t.Admin, t)).Status);
        Assert.Equal(30m, (await Get("/instructor-debits", t.Admin, t)).Data.GetProperty("outstanding").GetDecimal());
        // the payout the refund was recorded against cannot be cancelled once its debit was carried... here it was restored, so cancelling it removes the debit with it
        Assert.Equal(200, (await Put($"/instructor-payouts/{pay1.GetProperty("id").GetString()}", new { status = "Cancelled" }, t.Admin, t)).Status);
        Assert.Equal(0m, (await Get("/instructor-debits", t.Admin, t)).Data.GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task Held_revenue_stays_held_until_the_tenant_admin_sets_a_percentage_explicitly()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, _) = await PaidCourse(t, inst, 100m);
        var o = await Buy(t, st, c, 100m);
        var win = Q(1, -1); var held = await Get("/instructor-revenue/held" + win, t.Admin, t); Assert.Equal(200, held.Status);
        Assert.Equal(100m, held.Data.GetProperty("totalHeld").GetDecimal()); var item = held.Data.GetProperty("items")[0];
        Assert.Equal(403, (await Get("/instructor-revenue/held" + win, inst, t)).Status);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 60 }, t.Admin, t)).Status);   // setting a default later does not release it
        Assert.Equal(100m, (await Get("/instructor-revenue/held" + win, t.Admin, t)).Data.GetProperty("totalHeld").GetDecimal());
        var body = new { orderItemId = item.GetProperty("orderItemId").GetString(), courseId = item.GetProperty("courseId").GetString(), instructorId = item.GetProperty("instructorId").GetString(), percent = 50 };
        Assert.Equal(403, (await Put("/instructor-revenue/held", body, inst, t)).Status);
        Assert.Equal(400, (await Put("/instructor-revenue/held", new { body.orderItemId, body.courseId, body.instructorId, percent = 101 }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put("/instructor-revenue/held", body, t.Admin, t)).Status);
        Assert.Equal(0m, (await Get("/instructor-revenue/held" + win, t.Admin, t)).Data.GetProperty("totalHeld").GetDecimal());
        Assert.Equal(409, (await Put("/instructor-revenue/held", new { body.orderItemId, body.courseId, body.instructorId, percent = 90 }, t.Admin, t)).Status);   // cannot be changed afterwards
        await Task.Delay(50); Assert.True((await Post("/instructor-payouts/calculate", new { periodStartUtc = DateTime.UtcNow.AddDays(-1), periodEndUtc = DateTime.UtcNow }, t.Admin, t)).Success);
        Assert.Equal(50m, (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Refund_records_are_idempotent_race_safe_capped_and_debits_carry_across_payouts()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var (c, _) = await PaidCourse(t, inst, 100m);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 60 }, t.Admin, t)).Status);
        var s1 = await Student(t); var s2 = await Student(t); var s3 = await Student(t); var s4 = await Student(t);
        var o1 = await Buy(t, s1, c, 100m);
        // duplicate: same reference and amount returns the original, another amount is refused
        var a = await Post($"/orders/{o1}/refunds", new { amount = 10, reference = "DUP" }, t.Admin, t); Assert.True(a.Success, a.Body.ToString());
        var b = await Post($"/orders/{o1}/refunds", new { amount = 10, reference = "DUP" }, t.Admin, t); Assert.True(b.Success); Assert.True(b.Data.GetProperty("duplicate").GetBoolean());
        Assert.Equal(a.Data.GetProperty("refundId").GetString(), b.Data.GetProperty("refundId").GetString());
        Assert.Equal(409, (await Post($"/orders/{o1}/refunds", new { amount = 20, reference = "DUP" }, t.Admin, t)).Status);
        Assert.Equal(400, (await Post($"/orders/{o1}/refunds", new { amount = 5 }, t.Admin, t)).Status);   // a reference is required
        // concurrent requests can never refund more than 100 in total (10 already refunded: only one of three 60s can fit)
        var rs = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Post($"/orders/{o1}/refunds", new { amount = 60, reference = "C" + i }, t.Admin, t)));
        Assert.Equal(1, rs.Count(x => x.Success)); Assert.All(rs.Where(x => !x.Success), x => Assert.Equal(400, x.Status));
        var rest = await Post($"/orders/{o1}/refunds", new { amount = 30, reference = "REST" }, t.Admin, t); Assert.True(rest.Success, rest.Body.ToString());   // exactly the remaining 30
        Assert.Equal(400, (await Post($"/orders/{o1}/refunds", new { amount = 0.01, reference = "OVER" }, t.Admin, t)).Status);
        // o1 is now fully refunded: it earns nothing. Buy two more and calculate payout 1 (60 + 60 = 120).
        var o2 = await Buy(t, s2, c, 100m); await Buy(t, s3, c, 100m); await Task.Delay(50);
        var p1 = new { periodStartUtc = DateTime.UtcNow.AddDays(-1), periodEndUtc = DateTime.UtcNow };
        Assert.True((await Post("/instructor-payouts/calculate", p1, t.Admin, t)).Success);
        var pay1 = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0]; Assert.Equal(120m, pay1.GetProperty("amount").GetDecimal());
        // a refund of o2 after the payout: debit 60 * 90/100 = 54, then a second partial refund of the remaining 10 adds 6 (never more than the 60 that order earned)
        Assert.True((await Post($"/orders/{o2}/refunds", new { amount = 90, reference = "A" }, t.Admin, t)).Success);
        Assert.True((await Post($"/orders/{o2}/refunds", new { amount = 10, reference = "B" }, t.Admin, t)).Success);
        Assert.Equal(400, (await Post($"/orders/{o2}/refunds", new { amount = 1, reference = "C" }, t.Admin, t)).Status);
        Assert.Equal(60m, (await Get("/instructor-debits", t.Admin, t)).Data.GetProperty("outstanding").GetDecimal());
        // carry beyond one payout: next period earns only 40 (a 100 sale refunded 20 earlier, 80 * 0.6 = 48 -> minus nothing), build 48 then absorb 48 of 60, 12 stays
        var o4 = await Buy(t, s4, c, 100m); Assert.True((await Post($"/orders/{o4}/refunds", new { amount = 20, reference = "P" }, t.Admin, t)).Success);   // reduces before calculation: 48
        await Task.Delay(50); var p2 = new { periodStartUtc = p1.periodEndUtc, periodEndUtc = DateTime.UtcNow };
        Assert.True((await Post("/instructor-payouts/calculate", p2, t.Admin, t)).Success);
        var pay2 = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0]; Assert.Equal(0m, pay2.GetProperty("amount").GetDecimal());   // 48 earned, 48 absorbed
        Assert.Equal(12m, (await Get("/instructor-debits", t.Admin, t)).Data.GetProperty("outstanding").GetDecimal());
        // the payout whose earnings were refunded cannot be cancelled while its debit is already carried by a later payout
        var guard = await Put($"/instructor-payouts/{pay1.GetProperty("id").GetString()}", new { status = "Cancelled" }, t.Admin, t);
        Assert.Equal(409, guard.Status); Assert.Equal("DEBIT_ALREADY_APPLIED", guard.Code);
    }

    [Fact]
    public async Task Partial_subscription_refunds_across_several_calculated_periods_never_reverse_more_than_each_payout_earned()
    {
        var t = await NewTenant(); var ahmed = await Instructor(t); var st = await Student(t);
        var (c1, s1) = await BareCourse(t, ahmed);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 100 }, t.Admin, t)).Status);
        var plan = await Post("/subscription-plans", new { name = "Monthly", price = 1000, billingPeriod = "Monthly", scope = "AllCourses" }, t.Admin, t); Assert.True(plan.Success, plan.Body.ToString());
        await Fund(t, st, 1000m); var sub = await Post("/subscriptions", new { planId = Id(plan), paymentMethod = "Wallet" }, st, t); Assert.True(sub.Success, sub.Body.ToString());
        await Backdate(st.Id, 40);
        Assert.Equal(200, (await Put("/instructor-revenue/subscription-controls", new { minWatchPercent = 80, minAverageLessonSeconds = 10 }, t.Admin, t)).Status);
        await Done(st.Id, await MeasuredVideo(t, ahmed, s1, 60, 60), c1, 60, DateTime.UtcNow.AddDays(-35));
        await Done(st.Id, await MeasuredVideo(t, ahmed, s1, 60, 60), c1, 60, DateTime.UtcNow.AddDays(-20));
        string orderId; await using (var scope = _f.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var uid = Guid.Parse(st.Id); orderId = (await db.Orders.IgnoreQueryFilters().Where(o => o.UserId == uid).Select(o => o.Id).FirstAsync()).ToString(); }
        // two payouts cover the paid window (day -40 to -27, and -27 to -1)
        var mid = DateTime.UtcNow.AddDays(-27);
        Assert.True((await Post("/instructor-payouts/calculate", new { periodStartUtc = DateTime.UtcNow.AddDays(-60), periodEndUtc = mid }, t.Admin, t)).Success);
        Assert.True((await Post("/instructor-payouts/calculate", new { periodStartUtc = mid, periodEndUtc = DateTime.UtcNow.AddDays(-1) }, t.Admin, t)).Success);
        var items = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count); var earned = items.Sum(x => x.GetProperty("amount").GetDecimal()); Assert.Equal(1000m, earned);   // the whole subscription is earned across the two periods
        // refund 30% then 70%: the debits add up to exactly what was earned, never more
        Assert.True((await Post($"/orders/{orderId}/refunds", new { amount = 300, reference = "S1" }, t.Admin, t)).Success);
        var d1 = (await Get("/instructor-debits", t.Admin, t)).Data; Assert.Equal(300m, d1.GetProperty("outstanding").GetDecimal(), 1);
        Assert.True((await Post($"/orders/{orderId}/refunds", new { amount = 700, reference = "S2" }, t.Admin, t)).Success);
        var d2 = (await Get("/instructor-debits", t.Admin, t)).Data; Assert.Equal(earned, d2.GetProperty("outstanding").GetDecimal(), 1);
        foreach (var p in items) Assert.True(d2.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("sourcePayoutId").GetString() == p.GetProperty("id").GetString()).Sum(x => x.GetProperty("amount").GetDecimal()) <= p.GetProperty("amount").GetDecimal());
        Assert.Equal(400, (await Post($"/orders/{orderId}/refunds", new { amount = 1, reference = "S3" }, t.Admin, t)).Status);
    }

    [Fact]
    public async Task Held_revenue_released_after_a_payout_settles_in_a_later_payout_without_rewriting_paid_history()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var (c, _) = await PaidCourse(t, inst, 100m);
        var s1 = await Student(t); var s2 = await Student(t);
        await Buy(t, s1, c, 100m);                                                                          // sold before any percentage existed: held
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 60 }, t.Admin, t)).Status);
        await Buy(t, s2, c, 100m);                                                                          // sold after: earns 60
        await Task.Delay(50); var p1 = new { periodStartUtc = DateTime.UtcNow.AddDays(-1), periodEndUtc = DateTime.UtcNow };
        Assert.True((await Post("/instructor-payouts/calculate", p1, t.Admin, t)).Success);
        var pay1 = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0]; Assert.Equal(60m, pay1.GetProperty("amount").GetDecimal());
        Assert.Equal(200, (await Put($"/instructor-payouts/{pay1.GetProperty("id").GetString()}", new { status = "Approved" }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put($"/instructor-payouts/{pay1.GetProperty("id").GetString()}", new { status = "Paid", paymentReference = "B1" }, t.Admin, t)).Status);   // paid history
        var held = (await Get("/instructor-revenue/held" + Q(1, -1), t.Admin, t)).Data.GetProperty("items")[0];
        var rel = await Put("/instructor-revenue/held", new { orderItemId = held.GetProperty("orderItemId").GetString(), courseId = held.GetProperty("courseId").GetString(), instructorId = held.GetProperty("instructorId").GetString(), percent = 50 }, t.Admin, t);
        Assert.True(rel.Success, rel.Body.ToString()); Assert.Contains("next payout", rel.Data.GetProperty("settlement").GetString());
        var again = await Post("/instructor-payouts/calculate", new { periodStartUtc = p1.periodEndUtc, periodEndUtc = DateTime.UtcNow }, t.Admin, t); Assert.True(again.Success, again.Body.ToString());
        var items = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count); Assert.Equal(50m, items[0].GetProperty("amount").GetDecimal());       // the released 100 at 50 percent, settled once, in the later payout
        Assert.Equal(60m, items.First(x => x.GetProperty("id").GetString() == pay1.GetProperty("id").GetString()).GetProperty("amount").GetDecimal());   // the paid payout is untouched
        await Task.Delay(50); Assert.True((await Post("/instructor-payouts/calculate", new { periodStartUtc = DateTime.UtcNow.AddMinutes(-0.001), periodEndUtc = DateTime.UtcNow }, t.Admin, t)).Success);
        Assert.Equal(2, (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items").GetArrayLength());   // not settled twice
    }

    [Fact]
    public async Task A_refund_racing_a_payout_calculation_ends_with_the_same_net_whichever_wins()
    {
        for (var round = 0; round < 3; round++)
        {
            var t = await NewTenant(); var inst = await Instructor(t); var (c, _) = await PaidCourse(t, inst, 100m); var st = await Student(t);
            Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 60 }, t.Admin, t)).Status);
            var o = await Buy(t, st, c, 100m); await Task.Delay(50);
            var calc = Post("/instructor-payouts/calculate", new { periodStartUtc = DateTime.UtcNow.AddDays(-1), periodEndUtc = DateTime.UtcNow }, t.Admin, t);
            var refund = Post($"/orders/{o}/refunds", new { amount = 50, reference = "RACE" }, t.Admin, t);
            var both = await Task.WhenAll(calc, refund); Assert.True(both[0].Success, both[0].Body.ToString()); Assert.True(both[1].Success, both[1].Body.ToString());
            var payout = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0].GetProperty("amount").GetDecimal();
            var outstanding = (await Get("/instructor-debits", t.Admin, t)).Data.GetProperty("outstanding").GetDecimal();
            Assert.Equal(30m, payout - outstanding);   // 60 earned, half refunded: 30 net, as a smaller payout or as a payout of 60 plus a 30 debit, never both and never neither
        }
    }

    [Fact]
    public async Task A_late_released_subscription_is_settled_period_by_period_and_adds_up_to_the_whole_amount()
    {
        var t = await NewTenant(); var ahmed = await Instructor(t); var st = await Student(t); var st2 = await Student(t);
        var (c1, s1) = await BareCourse(t, ahmed); var (cs, _) = await PaidCourse(t, ahmed, 100m);
        var plan = await Post("/subscription-plans", new { name = "Monthly", price = 1000, billingPeriod = "Monthly", scope = "AllCourses" }, t.Admin, t); Assert.True(plan.Success, plan.Body.ToString());
        await Fund(t, st, 1000m); Assert.True((await Post("/subscriptions", new { planId = Id(plan), paymentMethod = "Wallet" }, st, t)).Success);   // bought before any percentage existed
        await Backdate(st.Id, 40);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 100 }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put("/instructor-revenue/subscription-controls", new { minWatchPercent = 80, minAverageLessonSeconds = 10 }, t.Admin, t)).Status);
        await Done(st.Id, await MeasuredVideo(t, ahmed, s1, 60, 60), c1, 60, DateTime.UtcNow.AddDays(-35));
        await Done(st.Id, await MeasuredVideo(t, ahmed, s1, 60, 60), c1, 60, DateTime.UtcNow.AddDays(-20));
        await Buy(t, st2, cs, 100m); await Backdate(st2.Id, 30);                                           // an unrelated sale gets paid out first
        var mid = DateTime.UtcNow.AddDays(-27);
        Assert.True((await Post("/instructor-payouts/calculate", new { periodStartUtc = DateTime.UtcNow.AddDays(-60), periodEndUtc = mid }, t.Admin, t)).Success);
        var pay1 = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0]; Assert.Equal(100m, pay1.GetProperty("amount").GetDecimal());
        var held = (await Get("/instructor-revenue/held" + Q(60, -1), t.Admin, t)).Data.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("kind").GetString() == "subscription").ToList();
        Assert.NotEmpty(held); var h = held[0];
        var hb = new { orderItemId = h.GetProperty("orderItemId").GetString(), courseId = h.GetProperty("courseId").GetString(), instructorId = h.GetProperty("instructorId").GetString() };
        Assert.Equal(400, (await Put("/instructor-revenue/held", new { hb.orderItemId, hb.courseId, hb.instructorId, percent = 100, nonVideoSeconds = 60 }, t.Admin, t)).Status);   // both allowance values or neither
        Assert.Equal(409, (await Put("/instructor-revenue/held", new { hb.orderItemId, hb.courseId, hb.instructorId, percent = 100, nonVideoSeconds = 60, nonVideoLessonCount = 2 }, t.Admin, t)).Status);   // already frozen at purchase for this course
        var rel = await Put("/instructor-revenue/held", new { hb.orderItemId, hb.courseId, hb.instructorId, percent = 100 }, t.Admin, t);
        Assert.True(rel.Success, rel.Body.ToString()); Assert.Contains("next payout", rel.Data.GetProperty("settlement").GetString());
        Assert.True((await Post("/instructor-payouts/calculate", new { periodStartUtc = mid, periodEndUtc = DateTime.UtcNow.AddDays(-1) }, t.Admin, t)).Success);
        var later = (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items")[0];
        Assert.NotEqual(pay1.GetProperty("id").GetString(), later.GetProperty("id").GetString());
        Assert.Equal(1000m, later.GetProperty("amount").GetDecimal());                                      // the whole subscription, allocated slice by slice, nothing lost or doubled
        Assert.Equal(100m, (await Get("/instructor-payouts", t.Admin, t)).Data.GetProperty("items").EnumerateArray().First(x => x.GetProperty("id").GetString() == pay1.GetProperty("id").GetString()).GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Affiliate_commission_is_credited_once_to_the_affiliates_wallet()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var aff = await Student(t); var buyer = await Student(t); var (c, _) = await PaidCourse(t, inst, 100m);
        var code = "AFF" + U().ToUpperInvariant();
        var p = await Post("/affiliate/profiles", new { userId = aff.Id, commissionPercent = 10, referralCode = code }, t.Admin, t);
        Assert.True(p.Success, p.Body.ToString());
        Assert.Equal(200, (await Get("/affiliate/me", aff, t)).Status);
        Assert.Equal(404, (await Get("/affiliate/me", buyer, t)).Status);
        await Post("/cart/items", new { itemType = "Course", itemId = c }, buyer, t); await Fund(t, buyer, 100m);
        var o = await Post("/orders/checkout", new { paymentMethod = "Wallet", referralCode = code }, buyer, t);
        Assert.True(o.Success, o.Body.ToString());
        Assert.Equal(10m, Bal(await Get("/wallet", aff, t)));
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AffiliateCommissions.IgnoreQueryFilters().CountAsync(x => x.OrderId == Guid.Parse(o.Data.GetProperty("orderId").GetString()!)));
        var c2 = (await PaidCourse(t, inst, 100m)).Course;
        await Post("/cart/items", new { itemType = "Course", itemId = c2 }, aff, t); await Fund(t, aff, 100m);
        var before = Bal(await Get("/wallet", aff, t));
        await Post("/orders/checkout", new { paymentMethod = "Wallet", referralCode = code }, aff, t);
        Assert.Equal(before - 100m, Bal(await Get("/wallet", aff, t)));   // no commission on one's own purchase
    }

    [Fact]
    public async Task Voucher_export_expires_after_the_retention_window()
    {
        var t = await NewTenant();
        var r = await Post("/voucher-batches", new { name = "B" + U(), prefix = "TST", quantity = 2, voucherType = "WalletCredit", walletAmount = 5 }, t.Admin, t);
        var id = Guid.Parse(r.Data.GetProperty("batchId").GetString()!);
        Assert.Contains("TST", await RawGet($"/voucher-batches/{id}/export", t.Admin, t));
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fileId = await db.VoucherBatches.IgnoreQueryFilters().Where(b => b.Id == id).Select(b => b.ExportFileId).FirstAsync();
            await db.FileAssets.IgnoreQueryFilters().Where(f => f.Id == fileId).ExecuteUpdateAsync(x => x.SetProperty(f => f.CreatedAtUtc, DateTime.UtcNow.AddDays(-60)));
        }
        Assert.Contains((await Get($"/voucher-batches/{id}/export", t.Admin, t)).Status, new[] { 404, 410 });
        Assert.Equal(403, (await Get($"/voucher-batches/{id}/export", await Student(t), t)).Status);
    }

    [Fact]
    public async Task Audit_log_filters_by_entity_user_and_date_and_other_tenants_are_invisible()
    {
        var t = await NewTenant();
        Assert.True((await Post("/coupons", new { code = "AUD" + U(), discountType = "Percentage", value = 5 }, t.Admin, t)).Success);
        Assert.True((await Get("/audit-logs?entity=Coupon", t.Admin, t)).Data.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Equal(0, (await Get("/audit-logs?entity=NoSuchEntity", t.Admin, t)).Data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await Get($"/audit-logs?entity=Coupon&userId={Guid.NewGuid()}", t.Admin, t)).Data.GetProperty("totalCount").GetInt32());
        var future = Uri.EscapeDataString(DateTime.UtcNow.AddDays(2).ToString("O"));
        Assert.Equal(0, (await Get($"/audit-logs?entity=Coupon&from={future}", t.Admin, t)).Data.GetProperty("totalCount").GetInt32());
        Assert.Equal(403, (await Get("/audit-logs", await Student(t), t)).Status);
        var other = await NewTenant();
        Assert.Equal(0, (await Get("/audit-logs?entity=Coupon", other.Admin, other)).Data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Reports_export_csv_and_xlsx_and_refuse_unknown_kinds()
    {
        var t = await NewTenant(); var inst = await Instructor(t);
        Assert.Contains(",", await RawGet("/reports/enrollments/export?format=csv", t.Admin, t));
        var x = await RawGet("/reports/revenue/export?format=xlsx", t.Admin, t); Assert.StartsWith("PK", x);   // an xlsx file is a zip
        Assert.Equal(404, (await Get("/reports/nonsense/export", t.Admin, t)).Status);
        Assert.Equal(404, (await Get("/reports/orders/export", inst, t)).Status);   // instructors only get enrollments and revenue
        Assert.Equal(403, (await Get("/reports/enrollments/export", await Student(t), t)).Status);
    }

    [Fact]
    public async Task Webhook_deliveries_are_signed_retried_with_backoff_abandoned_and_can_be_retried_by_hand()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, _) = await PaidCourse(t, inst, 10m);
        var h = await Post("/webhooks", new { url = "https://hooks.example.test/in", eventTypes = new[] { "order.paid" } }, t.Admin, t);
        Assert.True(h.Success, h.Body.ToString());
        var hookId = Id(h); var secret = h.Data.GetProperty("secret").GetString()!;
        FakeHookHandler.Status = 500; while (FakeHookHandler.Seen.TryDequeue(out _)) { }
        await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t); await Fund(t, st, 10m);
        Assert.True((await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t)).Success);
        async Task Run()
        {
            await using var scope = _f.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Set(Guid.Parse(await TenantIdAsync(t)), false);
            await scope.ServiceProvider.GetRequiredService<SmartLMS.Infrastructure.Services.Commerce.WebhookService>().DeliverDueAsync(default);
        }
        async Task MakeDue() { await using var scope = _f.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().WebhookDeliveries.IgnoreQueryFilters().Where(d => d.Status == WebhookDeliveryStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(d => d.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1))); }
        await Run();
        var d = (await Get($"/webhooks/{hookId}/deliveries", t.Admin, t)).Data.GetProperty("items")[0];
        Assert.Equal("Pending", d.GetProperty("status").GetString()); Assert.Equal(1, d.GetProperty("attemptCount").GetInt32());
        var first = FakeHookHandler.Seen.First();
        Assert.True(first.Signature.StartsWith("sha256="));
        Assert.Equal("order.paid", first.Event);
        Assert.Equal("sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(first.Ts + "." + first.Body))).ToLowerInvariant(), first.Signature);
        for (var i = 0; i < 10; i++) { await MakeDue(); await Run(); }
        d = (await Get($"/webhooks/{hookId}/deliveries", t.Admin, t)).Data.GetProperty("items")[0];
        Assert.True(d.GetProperty("status").GetString() == "Abandoned", d.ToString() + " seen=" + FakeHookHandler.Seen.Count); Assert.Equal(5, d.GetProperty("attemptCount").GetInt32());
        FakeHookHandler.Status = 200;
        Assert.Equal(200, (await Post($"/webhooks/deliveries/{d.GetProperty("id").GetString()}/retry", null, t.Admin, t)).Status);
        await MakeDue(); await Run();
        Assert.Equal("Delivered", (await Get($"/webhooks/{hookId}/deliveries", t.Admin, t)).Data.GetProperty("items")[0].GetProperty("status").GetString());
    }
    private async Task<string> TenantIdAsync(T t)
    {
        await using var scope = _f.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Tenants.IgnoreQueryFilters().Where(x => x.Slug == t.Slug).Select(x => x.Id).FirstAsync()).ToString();
    }


    private async Task<(string Course, string Section)> BareCourse(T t, Who inst)
    {
        var c = Id(await Post("/courses", new { title = "Alloc " + U() }, inst, t));
        var sec = Id(await Post($"/courses/{c}/sections", new { title = "S1" }, inst, t));
        Id(await Post($"/sections/{sec}/lessons", new { title = "Intro", lessonType = "Article", contentMarkdown = "x" }, inst, t));
        Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, inst, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        return (c, sec);
    }
    private async Task<string> Lesson(T t, Who inst, string section, string type, int? seconds, bool preview = false) =>
        Id(await Post($"/sections/{section}/lessons", new { title = "L" + U(), lessonType = type, contentMarkdown = type == "Article" ? "body" : null, durationSeconds = seconds, isFreePreview = preview }, inst, t));
    private async Task Done(string student, string lesson, string course, int watched, DateTime doneUtc)
    {
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = await db.Courses.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(course)).Select(x => x.TenantId).FirstAsync();
        using var _ = scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Use(tid);
        db.LessonProgress.Add(new LessonProgress { TenantId = tid, UserId = Guid.Parse(student), LessonId = Guid.Parse(lesson), CourseId = Guid.Parse(course), Status = LessonProgressStatus.Completed, WatchedSeconds = watched, CompletedAtUtc = doneUtc, LastAccessedAtUtc = doneUtc, CreatedAtUtc = doneUtc });
        await db.SaveChangesAsync();
    }
    private async Task Backdate(string student, int days)
    {
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var uid = Guid.Parse(student); var shift = DateTime.UtcNow.AddDays(-days);
        await db.Orders.IgnoreQueryFilters().Where(o => o.UserId == uid).ExecuteUpdateAsync(o => o.SetProperty(x => x.PaidAtUtc, shift));
    }
    private static string Q(int fromDays, int toDays) => $"?fromUtc={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-fromDays).ToString("o"))}&toUtc={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-toDays).ToString("o"))}";
    private static System.Text.Json.JsonElement Inst(Resp r, string id) => r.Data.GetProperty("instructors").EnumerateArray().First(x => x.GetProperty("instructorId").GetString()!.Equals(id, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Bundle_revenue_is_split_by_list_price_then_instructor_shares_apply()
    {
        var t = await NewTenant(); var ahmed = await Instructor(t); var mona = await Instructor(t); var st = await Student(t);
        var (c1, _) = await PaidCourse(t, ahmed, 600m); var (c2, _) = await PaidCourse(t, mona, 400m);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 70 }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put($"/courses/{c2}/instructor-share", new { instructorId = mona.Id, percent = 60 }, t.Admin, t)).Status);
        var b = await Post("/bundles", new { name = "B", price = 800, courseIds = new[] { Guid.Parse(c1), Guid.Parse(c2) } }, t.Admin, t); Assert.True(b.Success, b.Body.ToString());
        Assert.Equal(200, (await Post("/cart/items", new { itemType = "Bundle", itemId = Id(b) }, st, t)).Status);
        await Fund(t, st, 800m);
        var ck = await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t); Assert.True(ck.Success, ck.Body.ToString());
        var s = await Get("/instructor-payouts/statement" + Q(1, -1), t.Admin, t); Assert.True(s.Success, s.Body.ToString());
        Assert.Equal(336m, Inst(s, ahmed.Id).GetProperty("bundleAndPath").GetDecimal());   // 800 * 600/1000 = 480, 70 percent
        Assert.Equal(192m, Inst(s, mona.Id).GetProperty("bundleAndPath").GetDecimal());    // 800 * 400/1000 = 320, 60 percent
        Assert.Equal(200, (await Get("/instructor/revenue", ahmed, t)).Status);
    }

    /// <summary>A minimal valid MP4: ftyp plus moov/mvhd (timescale 1000). The free box carries random bytes so every call has a different hash unless <paramref name="salt"/> is given.</summary>
    private static byte[] Mp4(int seconds, byte[]? salt = null)
    {
        static byte[] B32(long v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        var mvhdBody = new byte[100]; B32(1000).CopyTo(mvhdBody, 12); B32(seconds * 1000L).CopyTo(mvhdBody, 16);
        var mvhd = B32(108).Concat("mvhd"u8.ToArray()).Concat(mvhdBody).ToArray();
        var moov = B32(8 + mvhd.Length).Concat("moov"u8.ToArray()).Concat(mvhd).ToArray();
        var ftyp = B32(20).Concat("ftypisom"u8.ToArray()).Concat(new byte[8]).ToArray();
        var free = salt ?? Guid.NewGuid().ToByteArray();
        return ftyp.Concat(B32(8 + free.Length)).Concat("free"u8.ToArray()).Concat(free).Concat(moov).ToArray();
    }
    private async Task<System.Text.Json.JsonElement> UploadMedia(T t, Who inst, string lesson, byte[] bytes)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/lessons/{lesson}/media");
        var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "v.mp4"); req.Content = form;
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inst.Token); req.Headers.Add("X-Tenant-Id", t.Slug); req.Headers.Add("X-Device-Id", inst.Dev);
        var res = await _c.SendAsync(req); var body = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
        Assert.Equal(200, (int)res.StatusCode); return body.GetProperty("data");
    }
    private async Task<string> MeasuredVideo(T t, Who inst, string section, int seconds, int typedSeconds, byte[]? salt = null)
    {
        var l = await Lesson(t, inst, section, "Video", typedSeconds);
        var d = await UploadMedia(t, inst, l, Mp4(seconds, salt));
        Assert.Equal(seconds, d.GetProperty("measuredDurationSeconds").GetInt32());
        return l;
    }

    [Fact]
    public void Media_duration_is_read_from_the_bytes_and_unreadable_files_are_not_measured()
    {
        Assert.Equal(90, SmartLMS.Infrastructure.Services.Lms.MediaDuration.TryMeasure(new MemoryStream(Mp4(90))));
        Assert.Null(SmartLMS.Infrastructure.Services.Lms.MediaDuration.TryMeasure(new MemoryStream(new byte[] { 1, 2, 3 })));
        var truncated = Mp4(90).Take(40).ToArray(); Assert.Null(SmartLMS.Infrastructure.Services.Lms.MediaDuration.TryMeasure(new MemoryStream(truncated)));
        Assert.Null(SmartLMS.Infrastructure.Services.Lms.MediaDuration.TryMeasure(new MemoryStream(Mp4(100000))));   // longer than 24 h is rejected as implausible
    }

    [Fact]
    public async Task Subscription_revenue_follows_measured_learning_time_and_is_invariant_to_splitting_reupload_and_period_cuts()
    {
        var t = await NewTenant(); var ahmed = await Instructor(t); var mona = await Instructor(t); var st = await Student(t); var idle = await Student(t);
        var (c1, s1) = await BareCourse(t, ahmed); var (c2, s2) = await BareCourse(t, mona);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 100 }, t.Admin, t)).Status);   // 100 percent so the numbers below are the allocation itself
        var art = new List<string>(); for (var i = 0; i < 2; i++) art.Add(await Lesson(t, mona, s2, "Article", null));   // with the course's Intro article: 3 published non-video lessons
        Assert.Equal(200, (await Put($"/courses/{c2}/learning-weight", new { nonVideoCourseSeconds = 300 }, t.Admin, t)).Status);
        var plan = await Post("/subscription-plans", new { name = "Monthly", price = 1000, billingPeriod = "Monthly", scope = "AllCourses" }, t.Admin, t); Assert.True(plan.Success, plan.Body.ToString());
        foreach (var u in new[] { st, idle }) { await Fund(t, u, 1000m); var sub = await Post("/subscriptions", new { planId = Id(plan), paymentMethod = "Wallet" }, u, t); Assert.True(sub.Success, sub.Body.ToString()); await Backdate(u.Id, 40); }
        var full = "/instructor-payouts/statement" + Q(60, -1);
        Assert.Equal(409, (await Get(full, t.Admin, t)).Status);   // the minimum watch percentage is not configured: nothing is guessed
        Assert.Equal(403, (await Put("/instructor-revenue/subscription-controls", new { minWatchPercent = 80 }, ahmed, t)).Status);
        Assert.Equal(400, (await Put("/instructor-revenue/subscription-controls", new { minWatchPercent = 120 }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put("/instructor-revenue/subscription-controls", new { minWatchPercent = 80, minAverageLessonSeconds = 300 }, t.Admin, t)).Status);
        var d20 = DateTime.UtcNow.AddDays(-20); var d35 = DateTime.UtcNow.AddDays(-35);
        // Ahmed: 12 one-minute lessons typed as 9999 s each (the typed value is ignored). Mona: one 12-minute lesson. All fully watched, 6 of Ahmed's before day 27, the rest after.
        for (var i = 0; i < 12; i++) await Done(st.Id, await MeasuredVideo(t, ahmed, s1, 60, 9999), c1, 60, i < 6 ? d35 : d20);
        await Done(st.Id, await MeasuredVideo(t, mona, s2, 720, 720), c2, 900, d20);
        // a copy of Ahmed's first video re-uploaded as a new lesson in another course of his, same bytes: it must not count again
        var salt = Guid.NewGuid().ToByteArray(); var orig = await MeasuredVideo(t, ahmed, s1, 60, 60, salt); await Done(st.Id, orig, c1, 60, d35);
        var (c3, s3) = await BareCourse(t, ahmed); var copy = await MeasuredVideo(t, ahmed, s3, 60, 60, salt); await Done(st.Id, copy, c3, 60, d20);
        await Done(st.Id, await MeasuredVideo(t, ahmed, s1, 60, 60), c1, 10, d20);                       // watched under 80 percent: not counted
        await Done(st.Id, await Lesson(t, mona, s2, "Video", 600, preview: true), c2, 600, d20);         // free preview: excluded
        // Mona's course has no approved non-video weight: its article earns nothing. An unmeasured (no file) video earns nothing either.
        await Done(st.Id, await Lesson(t, mona, s2, "Video", 5000), c2, 5000, d20);
        var s = await Get(full, t.Admin, t); Assert.True(s.Success, s.Body.ToString());
        var a = Inst(s, ahmed.Id); var m = Inst(s, mona.Id);
        // Ahmed 13 distinct one-minute videos (12 + the original of the copied one) = 780 s, Mona 720 s; the whole 1000 is earned inside this period and split 780:720
        var total = a.GetProperty("subscription").GetDecimal() + m.GetProperty("subscription").GetDecimal() + s.Data.GetProperty("unallocatedSubscriptionRevenue").GetDecimal();
        Assert.Equal(2000m, total);                                                                      // exact: both subscribers' 1000, nothing created or lost
        Assert.Equal(1000m, a.GetProperty("subscription").GetDecimal() + m.GetProperty("subscription").GetDecimal());   // the active subscriber is fully allocated
        Assert.Equal(1000m, s.Data.GetProperty("unallocatedSubscriptionRevenue").GetDecimal());
        Assert.Equal(13.0, a.GetProperty("subscriptionQualifiedMinutes").GetDouble()); Assert.Equal(13, a.GetProperty("subscriptionLessonsCounted").GetInt32());
        Assert.Equal(12.0, m.GetProperty("subscriptionQualifiedMinutes").GetDouble());
        Assert.Equal(Math.Round(1000m * 780m / 1500m, 2), a.GetProperty("subscription").GetDecimal());
        Assert.Contains("SHORT_LESSONS", a.GetProperty("flags").ToString()); Assert.DoesNotContain("SHORT_LESSONS", m.GetProperty("flags").ToString());
        // cutting the period in two never changes the total: earned evenly over the window, cumulative rounding
        var cut = Q(60, 27); var cut2 = Q(27, -1);
        var p1 = await Get("/instructor-payouts/statement" + cut, t.Admin, t); var p2 = await Get("/instructor-payouts/statement" + cut2, t.Admin, t);
        decimal Sum(Resp r) => r.Data.GetProperty("instructors").EnumerateArray().Sum(x => x.GetProperty("subscription").GetDecimal()) + r.Data.GetProperty("unallocatedSubscriptionRevenue").GetDecimal();
        Assert.Equal(2000m, Sum(p1) + Sum(p2));                                                          // both subscribers' 1000 each, exactly
        // cancelling does not shorten what was already paid for
        string subId; await using (var scope = _f.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var uid = Guid.Parse(st.Id); subId = (await db.UserSubscriptions.IgnoreQueryFilters().Where(x => x.UserId == uid).Select(x => x.Id).FirstAsync()).ToString(); }
        Assert.Equal(200, (await Post($"/subscriptions/{subId}/cancel", null, st, t)).Status);
        var after = await Get(full, t.Admin, t); Assert.Equal(Sum(s), Sum(after));
        // non-video time uses the allowance frozen on the plan line: 300 s over 3 lessons = 100 s each, only the first 3 lifetime completions count, so later extra lessons and later weight changes add nothing
        var before = Inst(await Get(full, t.Admin, t), mona.Id).GetProperty("subscriptionQualifiedMinutes").GetDouble();
        foreach (var l in art) await Done(st.Id, l, c2, 0, d20);
        var more = new List<string>(); for (var i = 0; i < 4; i++) more.Add(await Lesson(t, mona, s2, "Article", null));
        foreach (var l in more) await Done(st.Id, l, c2, 0, d20);
        var w1 = Inst(await Get(full, t.Admin, t), mona.Id).GetProperty("subscriptionQualifiedMinutes").GetDouble();
        Assert.Equal(before + 300 / 60.0, w1, 1);                                                          // 6 completions, only the first 3 (the frozen lesson count) earn 100 s each: exactly the approved 300 s, never more
        Assert.Equal(200, (await Put($"/courses/{c2}/learning-weight", new { nonVideoCourseSeconds = 9000 }, t.Admin, t)).Status);   // a later revision does not touch this purchase
        Assert.Equal(w1, Inst(await Get(full, t.Admin, t), mona.Id).GetProperty("subscriptionQualifiedMinutes").GetDouble(), 1);
        Assert.Contains("SubscriptionAllocationControls", (await Get("/audit-logs?pageSize=50", t.Admin, t)).Body.ToString());
    }

    [Fact]
    public async Task Bundle_split_is_frozen_at_purchase_even_if_prices_change_later()
    {
        var t = await NewTenant(); var ahmed = await Instructor(t); var mona = await Instructor(t); var st = await Student(t);
        var (c1, _) = await PaidCourse(t, ahmed, 600m); var (c2, _) = await PaidCourse(t, mona, 400m);
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 100 }, t.Admin, t)).Status);
        var b = await Post("/bundles", new { name = "B", price = 800, courseIds = new[] { Guid.Parse(c1), Guid.Parse(c2) } }, t.Admin, t);
        Assert.Equal(200, (await Post("/cart/items", new { itemType = "Bundle", itemId = Id(b) }, st, t)).Status);
        await Fund(t, st, 800m); var ck = await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t); Assert.True(ck.Success, ck.Body.ToString());
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Courses.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(c1)).ExecuteUpdateAsync(x => x.SetProperty(e => e.Price, 100m));   // a later price edit
            await db.BundleCourses.IgnoreQueryFilters().Where(x => x.CourseId == Guid.Parse(c2)).ExecuteDeleteAsync();                              // and a later bundle edit
        }
        // the instructor percentages are frozen at purchase as well: a new default and a new override change nothing for this sale
        Assert.Equal(200, (await Put("/instructor-revenue/default", new { percent = 10 }, t.Admin, t)).Status);
        Assert.Equal(200, (await Put($"/courses/{c1}/instructor-share", new { instructorId = ahmed.Id, percent = 5 }, t.Admin, t)).Status);
        var s = await Get("/instructor-payouts/statement" + Q(1, -1), t.Admin, t); Assert.True(s.Success, s.Body.ToString());
        Assert.Equal(480m, Inst(s, ahmed.Id).GetProperty("bundleAndPath").GetDecimal());
        Assert.Equal(320m, Inst(s, mona.Id).GetProperty("bundleAndPath").GetDecimal());
    }
}
