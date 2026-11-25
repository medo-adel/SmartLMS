using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Hubs;

/// <summary>Group names carry the tenant id, so a guessed room id from another tenant never matches.</summary>
public static class Groups
{
    public static string User(Guid tenant, Guid user) => $"t:{tenant:N}:u:{user:N}";
    public static string Room(Guid tenant, Guid room) => $"t:{tenant:N}:r:{room:N}";
    public static string Live(Guid tenant, Guid session) => $"t:{tenant:N}:l:{session:N}";
}

/// <summary>
/// Runs before every hub method. The connection request passed the tenant and device middleware once; a long-lived socket must not outlive a
/// disabled account or a reset device, so every invocation re-checks both, and sets the tenant for that invocation's database scope.
/// </summary>
public sealed class HubSecurityFilter(ITenantContext tenant, ICurrentUser user, SmartLMS.API.Infrastructure.HubIdentity identity, ApplicationDbContext db, ITokenService tokens) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await Check(ctx.Context);
        return await next(ctx);
    }
    public async Task OnConnectedAsync(HubLifetimeContext ctx, Func<HubLifetimeContext, Task> next)
    {
        await Check(ctx.Context);
        await next(ctx);
    }
    private async Task Check(HubCallerContext c)
    {
        identity.Principal = c.User;
        var exp = c.User?.FindFirst("exp")?.Value;
        if (long.TryParse(exp, out var e) && DateTimeOffset.FromUnixTimeSeconds(e) < DateTimeOffset.UtcNow.AddSeconds(-30)) throw new HubException("TOKEN_EXPIRED");   // an old socket does not outlive its token
        if (user.UserId is not { } uid || user.TokenTenantId is not { } tid || user.DeviceId is not { } did) throw new HubException("UNAUTHORIZED");
        tenant.Set(tid, false);
        var http = c.GetHttpContext();
        var fp = http?.Request.Headers["X-Device-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fp)) fp = http?.Request.Query["device_id"].FirstOrDefault();
        var row = await db.UserDevices.IgnoreQueryFilters().AsNoTracking().Where(d => d.Id == did && d.UserId == uid && d.IsActive).Select(d => d.DeviceFingerprintHash).FirstOrDefaultAsync();
        if (row is null || string.IsNullOrWhiteSpace(fp) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tokens.Hash(fp)), Encoding.UTF8.GetBytes(row))) throw new HubException("DEVICE_MISMATCH");
        if (!await db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Id == uid && u.IsActive && !u.IsDeleted)) throw new HubException("ACCOUNT_DISABLED");
    }
}

public sealed class SignalRRealtime(IHubContext<NotificationsHub> n, IHubContext<ChatHub> c, IHubContext<LiveHub> l) : IRealtime
{
    public Task ToLiveAsync(Guid tenantId, Guid sessionId, string eventName, object payload) => l.Clients.Group(Groups.Live(tenantId, sessionId)).SendAsync(eventName, payload);
    public Task ToUserAsync(Guid tenantId, Guid userId, string eventName, object payload) => n.Clients.Group(Groups.User(tenantId, userId)).SendAsync(eventName, payload);
    public Task ToRoomAsync(Guid tenantId, Guid roomId, string eventName, object payload) => c.Clients.Group(Groups.Room(tenantId, roomId)).SendAsync(eventName, payload);
}

[Authorize]
public sealed class NotificationsHub(ICurrentUser user) : Hub
{
    // A client can only ever join its own user group; there is no method that takes a user id.
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, SmartLMS.API.Hubs.Groups.User(user.TokenTenantId!.Value, user.UserId!.Value));
        await base.OnConnectedAsync();
    }
}

[Authorize]
public sealed class ChatHub(CommunityService community, ICurrentUser user) : Hub
{
    public async Task JoinRoom(Guid roomId)
    {
        try { await community.AssertRoomMemberAsync(roomId, Context.ConnectionAborted); }
        catch (Exception ex) when (ex is not HubException) { throw new HubException("ROOM_NOT_FOUND"); }
        await Groups.AddToGroupAsync(Context.ConnectionId, SmartLMS.API.Hubs.Groups.Room(user.TokenTenantId!.Value, roomId));
    }
    public async Task LeaveRoom(Guid roomId) => await Groups.RemoveFromGroupAsync(Context.ConnectionId, SmartLMS.API.Hubs.Groups.Room(user.TokenTenantId!.Value, roomId));
    /// <summary>Saved through the same service as the REST endpoint (membership, length limit), which then pushes to the room.</summary>
    public async Task SendMessage(Guid roomId, string body)
    {
        try { await community.SendAsync(roomId, new ChatSendRequest(body), Context.ConnectionAborted); }
        catch (Exception ex) when (ex is not HubException) { throw new HubException("SEND_REFUSED"); }
    }
}

[Authorize]
public sealed class LiveHub(CommunityService community, ICurrentUser user) : Hub
{
    public async Task JoinSession(Guid sessionId)
    {
        try { await community.AssertLiveAccessAsync(sessionId, Context.ConnectionAborted); }
        catch (Exception ex) when (ex is not HubException) { throw new HubException("SESSION_NOT_FOUND"); }
        await Groups.AddToGroupAsync(Context.ConnectionId, SmartLMS.API.Hubs.Groups.Live(user.TokenTenantId!.Value, sessionId));
    }
    public async Task LeaveSession(Guid sessionId) => await Groups.RemoveFromGroupAsync(Context.ConnectionId, SmartLMS.API.Hubs.Groups.Live(user.TokenTenantId!.Value, sessionId));
}
