namespace SmartLMS.Application.Interfaces;

/// <summary>Pushes an event to connected clients. Group names always include the tenant id so one tenant can never receive another's events.</summary>
public interface IRealtime
{
    Task ToUserAsync(Guid tenantId, Guid userId, string eventName, object payload);
    Task ToLiveAsync(Guid tenantId, Guid sessionId, string eventName, object payload);
    Task ToRoomAsync(Guid tenantId, Guid roomId, string eventName, object payload);
}
