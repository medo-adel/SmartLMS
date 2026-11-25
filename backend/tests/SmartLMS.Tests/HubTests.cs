using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class HubTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public HubTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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
    private static string Id(Resp r) => r.Data.GetProperty("id").GetString()!;

    private async Task<(string Course, string Section, string Lesson)> Course(T t, Who inst, bool publish, string? drip = null, bool freePreview = false)
    {
        var c = Id(await Post("/courses", new { title = "Course " + U(), dripMode = drip }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S1" }, inst, t));
        var l = Id(await Post($"/sections/{s}/lessons", new { title = "L1", lessonType = "Article", contentMarkdown = "secret body", isFreePreview = freePreview }, inst, t));
        if (publish)
        {
            Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, inst, t)).Status);
            Assert.Equal(200, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        }
        return (c, s, l);
    }
    private async Task<string> Published(T t, Who inst)
    {
        var c = Id(await Post("/courses", new { title = "Intro Course" }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S" }, inst, t));
        await Post($"/sections/{s}/lessons", new { title = "L", lessonType = "Article", contentMarkdown = "x" }, inst, t);
        await Post($"/courses/{c}/submit-for-approval", null, inst, t); await Post($"/courses/{c}/approve", null, t.Admin, t);
        return c;
    }
    private HubConnection Hub(string path, Who w, string? token = null, string? device = null)
    {
        var b = new HubConnectionBuilder().WithUrl(new Uri(_f.Server.BaseAddress, $"{path}?device_id={Uri.EscapeDataString(device ?? w.Dev)}"), o =>
        {
            o.HttpMessageHandlerFactory = _ => _f.Server.CreateHandler();
            o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            o.AccessTokenProvider = () => Task.FromResult<string?>(token ?? w.Token);
        });
        return b.Build();
    }
    private static async Task<T2> Wait<T2>(TaskCompletionSource<T2> tcs) => await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

    [Fact]
    public async Task Hubs_refuse_missing_token_and_wrong_device()
    {
        var t = await NewTenant(); var a = await Student(t);
        await using var anon = Hub("/hubs/chat", a, token: "");
        await Assert.ThrowsAnyAsync<Exception>(() => anon.StartAsync());
        await using var wrongDevice = Hub("/hubs/chat", a, device: "fingerprint-somebody-else-0123456789");
        await Assert.ThrowsAnyAsync<Exception>(() => wrongDevice.StartAsync());
        await using var ok = Hub("/hubs/chat", a); await ok.StartAsync();
        Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected, ok.State);
    }

    [Fact]
    public async Task Chat_hub_delivers_to_members_only_and_rejects_non_members()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t); var outsider = await Student(t);
        var c = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t); await Post($"/courses/{c}/enroll", null, b, t);
        var room = (await Get("/chat/rooms", a, t)).Data[0].GetProperty("id").GetString()!; await Get("/chat/rooms", b, t);
        var gotB = new TaskCompletionSource<JsonElement>(); var gotOut = new TaskCompletionSource<JsonElement>();
        await using var hb = Hub("/hubs/chat", b); hb.On<JsonElement>("message", m => gotB.TrySetResult(m)); await hb.StartAsync(); await hb.InvokeAsync("JoinRoom", Guid.Parse(room));
        await using var ho = Hub("/hubs/chat", outsider); ho.On<JsonElement>("message", m => gotOut.TrySetResult(m)); await ho.StartAsync();
        var ex = await Assert.ThrowsAsync<HubException>(() => ho.InvokeAsync("JoinRoom", Guid.Parse(room)));
        Assert.Contains("ROOM_NOT_FOUND", ex.Message);
        await using var ha = Hub("/hubs/chat", a); await ha.StartAsync(); await ha.InvokeAsync("JoinRoom", Guid.Parse(room));
        await ha.InvokeAsync("SendMessage", Guid.Parse(room), "hello over hub");
        Assert.Equal("hello over hub", (await Wait(gotB)).GetProperty("body").GetString());
        await Assert.ThrowsAsync<HubException>(() => ho.InvokeAsync("SendMessage", Guid.Parse(room), "intrude"));
        await Task.Delay(300); Assert.False(gotOut.Task.IsCompleted);
        var stored = (await Get($"/chat/rooms/{room}/messages", a, t)).Data.GetProperty("messages"); Assert.Equal(1, stored.GetArrayLength());   // the hub message is persisted
    }

    [Fact]
    public async Task Notifications_hub_pushes_announcements_to_the_enrolled_student_only()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t);
        var c = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t);
        var gotA = new TaskCompletionSource<JsonElement>(); var gotB = new TaskCompletionSource<JsonElement>();
        await using var ha = Hub("/hubs/notifications", a); ha.On<JsonElement>("notification", m => gotA.TrySetResult(m)); await ha.StartAsync();
        await using var hb = Hub("/hubs/notifications", b); hb.On<JsonElement>("notification", m => gotB.TrySetResult(m)); await hb.StartAsync();
        Assert.Equal(200, (await Post("/announcements", new { courseId = c, title = "Exam moved", body = "b" }, inst, t)).Status);
        Assert.Equal("Exam moved", (await Wait(gotA)).GetProperty("title").GetString());
        await Task.Delay(300); Assert.False(gotB.Task.IsCompleted);
    }

    [Fact]
    public async Task Live_session_events_and_other_notification_types_are_pushed()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t); var outsider = await Student(t);
        var c = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t); await Post($"/courses/{c}/enroll", null, b, t);
        var notes = new System.Collections.Concurrent.ConcurrentQueue<string>(); var gotA = new TaskCompletionSource<bool>();
        await using var hn = Hub("/hubs/notifications", a); hn.On<JsonElement>("notification", m => { notes.Enqueue(m.GetProperty("type").GetString()!); if (m.GetProperty("type").GetString() == "LiveSession") gotA.TrySetResult(true); }); await hn.StartAsync();
        var created = await Post($"/courses/{c}/live-sessions", new { title = "Q&A", provider = "Zoom", joinUrl = "https://zoom.example/j/1", startsAtUtc = DateTime.UtcNow.AddDays(1), endsAtUtc = DateTime.UtcNow.AddDays(1).AddHours(1) }, inst, t);
        Assert.Equal(200, created.Status); var sid = Id(created);
        await Wait(gotA);                                     // students are told about the new session in real time
        var count = new TaskCompletionSource<int>(); var leak = new TaskCompletionSource<int>();
        await using var la = Hub("/hubs/live", a); la.On<JsonElement>("attendance", m => count.TrySetResult(m.GetProperty("attendees").GetInt32())); await la.StartAsync(); await la.InvokeAsync("JoinSession", Guid.Parse(sid));
        await using var lo = Hub("/hubs/live", outsider); lo.On<JsonElement>("attendance", m => leak.TrySetResult(1)); await lo.StartAsync();
        Assert.Contains("SESSION_NOT_FOUND", (await Assert.ThrowsAsync<HubException>(() => lo.InvokeAsync("JoinSession", Guid.Parse(sid)))).Message);
        Assert.Equal(200, (await Post($"/live-sessions/{sid}/join", null, a, t)).Status);
        Assert.Equal(1, await Wait(count));                   // head count only
        await Task.Delay(300); Assert.False(leak.Task.IsCompleted);
        // grading pushes a "Graded" notification
        var asg = Id(await Post($"/courses/{c}/assignments", new { title = "A", maxPoints = 10, isPublished = true }, inst, t));
        var sub = Id(await Post($"/assignments/{asg}/submissions", new { textContent = "work" }, a, t));
        Assert.Equal(200, (await Put($"/submissions/{sub}/grade", new { totalScore = 8.0, feedback = "good" }, inst, t)).Status);
        for (var i = 0; i < 40 && !notes.Contains("Graded"); i++) await Task.Delay(100);
        Assert.Contains("Graded", notes);
    }

    [Fact]
    public async Task Fcm_sender_signs_a_token_request_sends_a_message_and_reports_dead_tokens()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var json = JsonSerializer.Serialize(new { client_email = "svc@proj.iam.example", project_id = "proj-1", private_key = rsa.ExportPkcs8PrivateKeyPem(), token_uri = "https://oauth.example/token" });
        var h = new FakeFcm(); using var fcm = new SmartLMS.Infrastructure.Services.FcmPushSender(new HttpClient(h), json);
        Assert.Equal(SmartLMS.Application.Interfaces.PushResult.Sent, await fcm.SendAsync("good-token", "Hello", "Body", default));
        Assert.Equal(SmartLMS.Application.Interfaces.PushResult.Sent, await fcm.SendAsync("good-token", "Hello2", null, default));
        Assert.Equal(1, h.TokenCalls);                                                    // the access token is cached
        var jwt = h.Assertion!.Split('.'); Assert.Equal(3, jwt.Length);
        var pad = (string x) => x.Replace('-', '+').Replace('_', '/').PadRight((x.Length + 3) / 4 * 4, '=');
        Assert.True(rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(jwt[0] + "." + jwt[1]), Convert.FromBase64String(pad(jwt[2])), System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1));
        Assert.Contains("\"iss\":\"svc@proj.iam.example\"", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pad(jwt[1]))));
        Assert.Equal("Bearer fake-access", h.LastAuth); Assert.Contains("good-token", h.LastBody); Assert.Contains("/projects/proj-1/messages:send", h.LastUrl);
        Assert.Equal(SmartLMS.Application.Interfaces.PushResult.InvalidToken, await fcm.SendAsync("dead-token", "x", null, default));
        Assert.Equal(SmartLMS.Application.Interfaces.PushResult.Failed, await fcm.SendAsync("boom", "x", null, default));
    }
    private sealed class FakeFcm : HttpMessageHandler
    {
        public int TokenCalls; public string? Assertion, LastAuth, LastBody, LastUrl;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.RequestUri!.Host == "oauth.example") { TokenCalls++; var form = await r.Content!.ReadAsStringAsync(ct); Assertion = Uri.UnescapeDataString(form.Split("assertion=")[1]); return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fake-access\",\"expires_in\":3600}") }; }
            LastUrl = r.RequestUri.ToString(); LastAuth = r.Headers.Authorization?.ToString(); LastBody = await r.Content!.ReadAsStringAsync(ct);
            if (LastBody.Contains("dead-token")) return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"status\":\"NOT_FOUND\",\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}") };
            if (LastBody.Contains("boom")) return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"name\":\"m\"}") };
        }
    }

    [Fact]
    public async Task Websocket_transport_works_with_token_and_device_in_the_query()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t);
        var c = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t);
        var got = new TaskCompletionSource<JsonElement>();
        var url = new Uri(_f.Server.BaseAddress, $"/hubs/notifications?device_id={Uri.EscapeDataString(a.Dev)}");
        await using var h = new HubConnectionBuilder().WithUrl(url, o =>
        {
            o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets; o.SkipNegotiation = false;
            o.HttpMessageHandlerFactory = _ => _f.Server.CreateHandler();
            o.AccessTokenProvider = () => Task.FromResult<string?>(a.Token);
            o.WebSocketFactory = async (ctx, ct) =>
            {
                var wsc = _f.Server.CreateWebSocketClient();   // the token travels in the query string for WebSockets
                return await wsc.ConnectAsync(new Uri(ctx.Uri + (ctx.Uri.Query.Length > 0 ? "&" : "?") + "access_token=" + Uri.EscapeDataString(a.Token)), ct);
            };
        }).Build();
        h.On<JsonElement>("notification", m => got.TrySetResult(m)); await h.StartAsync();
        Assert.Equal(200, (await Post("/announcements", new { courseId = c, title = "Over websocket", body = "b" }, inst, t)).Status);
        Assert.Equal("Over websocket", (await Wait(got)).GetProperty("title").GetString());
    }

    private sealed class FakePush : SmartLMS.Application.Interfaces.IPushSender
    {
        public List<string> Sent = new();
        public Task<SmartLMS.Application.Interfaces.PushResult> SendAsync(string token, string title, string? body, CancellationToken ct)
        { Sent.Add(token); return Task.FromResult(token.StartsWith("dead") ? SmartLMS.Application.Interfaces.PushResult.InvalidToken : SmartLMS.Application.Interfaces.PushResult.Sent); }
    }

    [Fact]
    public async Task Notifier_stores_pushes_to_devices_and_drops_dead_tokens()
    {
        var t = await NewTenant(); var a = await Student(t);
        Assert.Equal(200, (await Post("/notifications/push-token", new { fcmToken = "live-token-1", platform = "Android" }, a, t)).Status);
        Assert.Equal(200, (await Post("/notifications/push-token", new { fcmToken = "dead-token-2", platform = "Ios" }, a, t)).Status);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartLMS.Infrastructure.Persistence.ApplicationDbContext>();
        var tid = (await db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.Slug == t.Slug)).Id;
        scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Set(tid, false);
        var fake = new FakePush();
        var n = new SmartLMS.Infrastructure.Services.Lms.Notifier(db, scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.IRealtime>(), fake);
        await n.SendAsync(tid, [Guid.Parse(a.Id)], "Test", "Hello", "Body", default);
        Assert.Equal(2, fake.Sent.Count);
        Assert.Equal(new[] { "live-token-1" }, await db.PushTokens.Where(x => x.UserId == Guid.Parse(a.Id)).Select(x => x.FcmToken).ToArrayAsync());   // the dead token was removed
        Assert.Equal(1, await db.Notifications.CountAsync(x => x.UserId == Guid.Parse(a.Id) && x.Type == "Test"));
    }
}
