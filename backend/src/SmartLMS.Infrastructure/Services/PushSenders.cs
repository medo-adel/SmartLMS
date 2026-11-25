using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartLMS.Application.Interfaces;

namespace SmartLMS.Infrastructure.Services;

public sealed class NullPushSender : IPushSender
{
    public Task<PushResult> SendAsync(string deviceToken, string title, string? body, CancellationToken ct) => Task.FromResult(PushResult.Disabled);
}

/// <summary>
/// Firebase Cloud Messaging HTTP v1. Enabled only with Push:Fcm:Enabled=true and Push:Fcm:ServiceAccountPath (a service-account JSON file that stays OUTSIDE the repository).
/// It signs an OAuth JWT with the service account key, exchanges it for an access token and caches that until shortly before expiry.
/// NOT run against the real FCM service in this project: it is tested against a fake HTTP server only.
/// </summary>
public sealed class FcmPushSender : IPushSender, IDisposable
{
    private readonly HttpClient _http; private readonly string _email, _project, _tokenUri; private readonly RSA _rsa;
    private string? _access; private DateTime _accessUntil; private readonly SemaphoreSlim _gate = new(1, 1);

    public FcmPushSender(HttpClient http, string serviceAccountJson)
    {
        _http = http; using var d = JsonDocument.Parse(serviceAccountJson); var r = d.RootElement;
        _email = r.GetProperty("client_email").GetString()!; _project = r.GetProperty("project_id").GetString()!;
        _tokenUri = r.TryGetProperty("token_uri", out var tu) ? tu.GetString()! : "https://oauth2.googleapis.com/token";
        _rsa = RSA.Create(); _rsa.ImportFromPem(r.GetProperty("private_key").GetString()!);
    }
    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string?> TokenAsync(CancellationToken ct)
    {
        if (_access is not null && DateTime.UtcNow < _accessUntil) return _access;
        await _gate.WaitAsync(ct);
        try
        {
            if (_access is not null && DateTime.UtcNow < _accessUntil) return _access;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var head = B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new { iss = _email, scope = "https://www.googleapis.com/auth/firebase.messaging", aud = _tokenUri, iat = now, exp = now + 3600 }));
            var sig = B64(_rsa.SignData(Encoding.UTF8.GetBytes(head + "." + claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var res = await _http.PostAsync(_tokenUri, new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = head + "." + claims + "." + sig }), ct);
            if (!res.IsSuccessStatusCode) return null;
            using var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            _access = d.RootElement.GetProperty("access_token").GetString(); _accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, d.RootElement.GetProperty("expires_in").GetInt32() - 120));
            return _access;
        }
        finally { _gate.Release(); }
    }

    public async Task<PushResult> SendAsync(string deviceToken, string title, string? body, CancellationToken ct)
    {
        try
        {
            var at = await TokenAsync(ct); if (at is null) return PushResult.Failed;
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{_project}/messages:send") { Content = new StringContent(JsonSerializer.Serialize(new { message = new { token = deviceToken, notification = new { title, body } } }), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", at);
            var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return PushResult.Sent;
            var text = await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode is 404 or 400 && (text.Contains("UNREGISTERED") || text.Contains("INVALID_ARGUMENT"))) return PushResult.InvalidToken;
            if ((int)res.StatusCode == 401) _access = null;
            return PushResult.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return PushResult.Failed; }
    }
    public void Dispose() { _rsa.Dispose(); _gate.Dispose(); }
}
