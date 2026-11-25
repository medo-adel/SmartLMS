using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

/// <summary>Stores a notification, then pushes it live over SignalR and, when FCM is enabled, to the user's mobile devices. Delivery problems never fail the caller.</summary>
public sealed class Notifier(ApplicationDbContext db, IRealtime rt, IPushSender push)
{
    public const int MaxLivePush = 500;

    public async Task SendAsync(Guid tenantId, IEnumerable<Guid> userIds, string type, string title, string? body, CancellationToken ct, string? dataJson = null)
    {
        var users = userIds.Distinct().ToList(); if (users.Count == 0) return;
        title = title.Length > 300 ? title[..300] : title; body = body is { Length: > 500 } ? body[..500] : body;
        foreach (var u in users) db.Notifications.Add(new Notification { TenantId = tenantId, UserId = u, Type = type, Title = title, Body = body, DataJson = dataJson, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        foreach (var u in users.Take(MaxLivePush))
        {
            try { await rt.ToUserAsync(tenantId, u, "notification", new { type, title, body }); } catch (Exception) { }
            try
            {
                var tokens = await db.PushTokens.Where(t => t.UserId == u).OrderByDescending(t => t.LastSeenAtUtc).Take(5).ToListAsync(ct);
                foreach (var t in tokens)
                    if (await push.SendAsync(t.FcmToken, title, body, ct) == PushResult.InvalidToken) { db.PushTokens.Remove(t); await db.SaveChangesAsync(ct); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { db.ChangeTracker.Clear(); }
        }
    }
}
