namespace SmartLMS.Application.Interfaces;

public enum PushResult { Sent, InvalidToken, Failed, Disabled }

/// <summary>Sends one mobile push notification. Implementations must never throw for a delivery problem.</summary>
public interface IPushSender
{
    Task<PushResult> SendAsync(string deviceToken, string title, string? body, CancellationToken ct);
}
