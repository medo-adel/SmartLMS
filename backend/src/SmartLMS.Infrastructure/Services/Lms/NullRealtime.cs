using SmartLMS.Application.Interfaces;
namespace SmartLMS.Infrastructure.Services.Lms;
/// <summary>Used when no hub is mapped (background hosts, tools). Pushes nothing.</summary>
public sealed class NullRealtime : IRealtime
{
    public Task ToUserAsync(Guid tenantId, Guid userId, string eventName, object payload) => Task.CompletedTask;
    public Task ToLiveAsync(Guid tenantId, Guid sessionId, string eventName, object payload) => Task.CompletedTask;
    public Task ToRoomAsync(Guid tenantId, Guid roomId, string eventName, object payload) => Task.CompletedTask;
}
