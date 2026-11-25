using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

public sealed class DeviceService(ApplicationDbContext db, ITokenService tokens, ICurrentUser currentUser, ICurrentTenant tenant, TimeProvider clock) : IDeviceService
{
    public async Task<IReadOnlyList<UserDeviceDto>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        await EnsureVisibleUser(userId, restricted: IsRestricted(userId), ct);
        return await db.UserDevices.AsNoTracking().Where(d => d.UserId == userId).OrderByDescending(d => d.LastSeenAtUtc)
            .Select(d => new UserDeviceDto(d.Id, d.DeviceType.ToString(), d.Platform, d.DeviceName, d.FirstSeenAtUtc, d.LastSeenAtUtc, d.IsActive)).ToListAsync(ct);
    }

    private bool IsRestricted(Guid targetUserId) =>
        currentUser.UserId != targetUserId && currentUser.IsInRole(RoleNames.Instructor) && !currentUser.IsInRole(RoleNames.TenantAdmin) && !currentUser.IsInRole(RoleNames.SuperAdmin);

    /// <summary>
    /// Restricted callers (Instructor) may only touch Student accounts they are explicitly related to: students with an enrollment in a course the instructor owns.
    /// No relationship means 404 (the account is not disclosed). TenantAdmin / owner / SuperAdmin-with-support-access are not restricted.
    /// </summary>
    private async Task EnsureVisibleUser(Guid userId, bool restricted, CancellationToken ct)
    {
        var exists = await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, ct);
        if (!exists) throw new NotFoundException("User not found.");
        if (!restricted) return;
        var me = currentUser.UserId ?? throw new UnauthorizedAppException();
        var related = await db.StudentsOfInstructor(me).AnyAsync(id => id == userId, ct);
        if (!related) throw new NotFoundException("User not found.");
    }

    public async Task RemoveOwnDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException();
        var device = await db.UserDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == uid, ct) ?? throw new NotFoundException("Device not found.");
        await RevokeAsync(device, uid, ct);
    }

    public async Task<int> ResetUserDevicesAsync(Guid userId, string? reason, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAppException();
        var tid = tenant.TenantId ?? throw new ValidationAppException("A tenant is required.");
        await EnsureVisibleUser(userId, restricted: IsRestricted(userId), ct);
        var isStudent = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == userId && r.Name == RoleNames.Student select ur).AnyAsync(ct);
        if (!isStudent) throw new ForbiddenAppException("FORBIDDEN", "Only student devices can be reset.");
        var now = clock.GetUtcNow().UtcDateTime;
        var devices = await db.UserDevices.Where(d => d.UserId == userId && d.IsActive).ToListAsync(ct);
        foreach (var d in devices)
        {
            d.IsActive = false;
            d.RevokedAtUtc = now;
            d.RevokedBy = actor;
        }
        var tokensToRevoke = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in tokensToRevoke) { t.RevokedAtUtc = now; t.RevokedReason = "device_reset"; }

        db.DeviceResetLogs.Add(new DeviceResetLog { TenantId = tid, UserId = userId, ResetByUserId = actor, Reason = reason, ResetAtUtc = now });
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tid, UserId = actor, Action = AuditLogAction.ResetDevice, EntityName = "User", EntityId = userId.ToString(),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { devicesReset = devices.Count, reason }), IpAddress = currentUser.IpAddress,
            CorrelationId = currentUser.CorrelationId, OccurredAtUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return devices.Count;
    }

    private async Task RevokeAsync(UserDevice device, Guid actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        device.IsActive = false;
        device.RevokedAtUtc = now;
        device.RevokedBy = actor;
        var rts = await db.RefreshTokens.Where(t => t.DeviceId == device.Id && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in rts) { t.RevokedAtUtc = now; t.RevokedReason = "device_removed"; }
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserDevice> BindAsync(Guid userId, Guid? tenantId, bool enforceCap, DeviceInfo info, CancellationToken ct)
    {
        var type = Enum.Parse<UserDeviceDeviceType>(info.DeviceType, ignoreCase: true);
        // The cap is "count active devices, then insert". Without a lock two simultaneous logins both count below the cap and both insert.
        // Taking a write lock on the user's own row first (held until commit) makes binds for one user run one at a time on SQL Server
        // and SQLite; other users are not affected.
        var ownTx = db.Database.CurrentTransaction is null;
        await using var tx = ownTx ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.UpdatedAtUtc, clock.GetUtcNow().UtcDateTime), ct);
        var device = await BindLockedAsync(userId, tenantId, enforceCap, info, type, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return device;
    }

    private async Task<UserDevice> BindLockedAsync(Guid userId, Guid? tenantId, bool enforceCap, DeviceInfo info, UserDeviceDeviceType type, CancellationToken ct)
    {
        var hash = tokens.Hash(info.Fingerprint);
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.UserDevices.FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceFingerprintHash == hash, ct);
        if (existing is { IsActive: true })
        {
            existing.LastSeenAtUtc = now;
            existing.LastIp = currentUser.IpAddress;
            existing.Platform = info.Platform ?? existing.Platform;
            existing.DeviceName = info.DeviceName ?? existing.DeviceName;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        if (enforceCap && tenantId is not null)
        {
            var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tenantId, ct);
            var cap = type == UserDeviceDeviceType.Mobile ? t.MaxMobileDevices : t.MaxWebDevices;
            var active = await db.UserDevices.CountAsync(d => d.UserId == userId && d.IsActive && d.DeviceType == type, ct);
            if (active >= cap)
                throw new ForbiddenAppException("DEVICE_LIMIT_REACHED", $"This account is already registered on the maximum number of {type.ToString().ToLowerInvariant()} devices ({cap}). Ask your administrator or instructor to reset your devices.");
        }

        if (existing is not null)
        {
            existing.IsActive = true;
            existing.RevokedAtUtc = null;
            existing.RevokedBy = null;
            existing.LastSeenAtUtc = now;
            existing.LastIp = currentUser.IpAddress;
            existing.DeviceType = type;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var device = new UserDevice
        {
            TenantId = tenantId, UserId = userId, DeviceFingerprintHash = hash, DeviceType = type, Platform = info.Platform, DeviceName = info.DeviceName,
            FirstSeenAtUtc = now, LastSeenAtUtc = now, LastIp = currentUser.IpAddress, IsActive = true,
        };
        db.UserDevices.Add(device);
        await db.SaveChangesAsync(ct);
        return device;
    }
}
