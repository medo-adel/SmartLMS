using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class CapturingOtpSender : IOtpSender
{
    public readonly Dictionary<string, string> Codes = new();
    public Task SendAsync(string destination, string purpose, string code, CancellationToken ct)
    {
        lock (Codes) Codes[$"{destination}|{purpose}"] = code;
        return Task.CompletedTask;
    }
    public string Last(string destination, string purpose) { lock (Codes) return Codes[$"{destination.ToLowerInvariant()}|{purpose}"]; }
}

public sealed class CapturingEmailTransport : IEmailTransport
{
    public bool IsEnabled => true;
    public string DeliveryMode => "captured";
    public readonly List<(string To, string Subject, string Body)> Sent = new();
    public Task SendAsync(EmailMessage message, CancellationToken ct) { lock (Sent) Sent.Add((message.To, message.Subject, message.Body)); return Task.CompletedTask; }
    public List<(string To, string Subject, string Body)> For(string to) { lock (Sent) return Sent.Where(m => m.To == to).ToList(); }
}

/// <summary>Boots the real API on in-memory SQLite. SQLite approximates SQL Server; it is not a substitute for a run against SQL Server.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string SaEmail = "root@platform.test";
    public const string Password = "Str0ngPassw0rd!x";
    // A file database (WAL) instead of :memory: so parallel requests use separate connections, as they do against SQL Server.
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"smartlms-test-{Guid.NewGuid():N}.db");
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"smartlms-storage-{Guid.NewGuid():N}");
    public CapturingOtpSender Otp { get; } = new();
    public CapturingEmailTransport Mail { get; } = new();

    public ApiFactory()
    {
        using var c = new SqliteConnection($"Data Source={_dbFile};Pooling=False;Default Timeout=60");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-0123456789-abcdefghij");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "100000");
        builder.UseSetting("Database:EnsureCreated", "true");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Hangfire:Storage", "InMemory");
        builder.UseSetting("Storage:Root", StorageRoot);
        // Paymob uses the real adapter with a fake HTTP handler (no network). Fawry exercises the generic signed-body adapter.
        foreach (var (k, v) in new[] { ("Enabled", "true"), ("PublicKey", "pk_test_x"), ("SecretKey", "sk_test_x"), ("HmacSecret", "test-paymob-hmac"), ("IntegrationId", "4569876"), ("NotificationUrl", "https://lms.example.test/api/v1/payments/gateways/paymob/webhook") })
            builder.UseSetting("Payments:Gateways:Paymob:" + k, v);
        builder.UseSetting("Webhooks:AllowPrivateTargets", "true");   // the SSRF address rules are unit-tested directly; here a fake handler replaces the network
        builder.UseSetting("Payments:Gateways:Fawry:Enabled", "true");
        builder.UseSetting("Payments:Gateways:Fawry:Secret", "test-gateway-secret");
        builder.UseSetting("Payments:Gateways:Fawry:CheckoutUrlTemplate", "https://pay.example.test/checkout/{reference}?a={amount}&c={currency}");
        builder.UseSetting("RateLimiting:VoucherPermitsPerMinute", "100000");
        builder.UseSetting("Certificates:VerificationBaseUrl", "https://lms.example.test");
        builder.UseSetting("Seed:SuperAdminEmail", SaEmail);
        builder.UseSetting("Seed:SuperAdminPassword", Password);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            s.RemoveAll(typeof(IDbContextOptionsConfiguration<ApplicationDbContext>));
            s.AddDbContext<ApplicationDbContext>((sp, o) =>
            {
                o.UseSqlite($"Data Source={_dbFile};Pooling=False;Default Timeout=60");
                o.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
            });
            s.AddHttpClient("webhooks").ConfigurePrimaryHttpMessageHandler(() => new FakeHookHandler());
            s.AddHttpClient("paymob").ConfigurePrimaryHttpMessageHandler(() => new FakePaymobHandler());
            s.RemoveAll<IOtpSender>();
            s.AddSingleton<IOtpSender>(Otp);
            s.RemoveAll<IEmailTransport>();
            s.AddSingleton<IEmailTransport>(Mail);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var f in new[] { _dbFile, _dbFile + "-wal", _dbFile + "-shm" }) { try { File.Delete(f); } catch { } }
        try { Directory.Delete(StorageRoot, true); } catch { }
    }
}

public sealed record Resp(int Status, JsonElement Body)
{
    public bool Success => Body.TryGetProperty("success", out var s) && s.GetBoolean();
    public string? Code => Body.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
    public JsonElement Data => Body.GetProperty("data");
}

public static class Http
{
    public static async Task<Resp> Send(this HttpClient c, HttpMethod m, string path, object? body = null, string? token = null, string? tenant = null, string? device = null)
    {
        var req = new HttpRequestMessage(m, "/api/v1" + path);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenant is not null) req.Headers.Add("X-Tenant-Id", tenant);
        if (device is not null) req.Headers.Add("X-Device-Id", device);
        var res = await c.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return new Resp((int)res.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }
    public static async Task<Resp> Upload(this HttpClient c, string path, byte[] bytes, string fileName, string? token, string? tenant, string? device)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1" + path);
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } };
        req.Content = form;
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenant is not null) req.Headers.Add("X-Tenant-Id", tenant);
        if (device is not null) req.Headers.Add("X-Device-Id", device);
        var res = await c.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return new Resp((int)res.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }
    public static Task<Resp> Post(this HttpClient c, string p, object? b = null, string? token = null, string? tenant = null, string? device = null) => c.Send(HttpMethod.Post, p, b ?? new { }, token, tenant, device);
    public static Task<Resp> Get(this HttpClient c, string p, string? token = null, string? tenant = null, string? device = null) => c.Send(HttpMethod.Get, p, null, token, tenant, device);
}

public sealed class FakePaymobHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"1\",\"client_secret\":\"csk_test_fake\",\"status\":\"intended\"}") });
}

public sealed class FakeHookHandler : HttpMessageHandler
{
    public static volatile int Status = 200;
    public static readonly System.Collections.Concurrent.ConcurrentQueue<(string Event, string Signature, string Body, string Ts)> Seen = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        Seen.Enqueue((r.Headers.GetValues("X-Webhook-Event").First(), r.Headers.GetValues("X-Webhook-Signature").First(), await r.Content!.ReadAsStringAsync(ct), r.Headers.GetValues("X-Webhook-Timestamp").First()));
        return new HttpResponseMessage((System.Net.HttpStatusCode)Status);
    }
}
