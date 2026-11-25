using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Common;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Identity;

namespace SmartLMS.Infrastructure.Persistence;

/// <summary>
/// Runs on every SaveChanges: (1) refuses writes into a tenant other than the current one, (2) turns deletes into soft deletes,
/// (3) stamps audit columns, (4) writes AuditLogs rows for entities marked [AuditedEntity] and for identity users, with secrets redacted.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentTenant tenant, ICurrentUser user, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly string[] Sensitive = ["hash", "secret", "password", "token", "stamp"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext db)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var actor = user.UserId;
        var entries = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var audits = new List<AuditLog>();

        foreach (var entry in entries)
        {
            if (entry.Entity is AuditLog) continue;
            GuardTenant(entry);

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable sd)
            {
                // Capture before flipping the state so the log still says "Deleted".
                var deleted = Snapshot(entry, deleting: true);
                entry.State = EntityState.Modified;
                sd.IsDeleted = true;
                sd.DeletedAtUtc = now;
                sd.DeletedBy = actor;
                if (entry.Entity is IAuditableEntity au0) { au0.UpdatedAtUtc = now; au0.UpdatedBy = actor; }
                if (IsAudited(entry.Entity)) audits.Add(NewLog(entry, AuditLogAction.Delete, deleted.Old, null));
                continue;
            }

            if (entry.Entity is IAuditableEntity au)
            {
                if (entry.State == EntityState.Added)
                {
                    au.CreatedAtUtc = now;
                    au.CreatedBy ??= actor;
                }
                else if (entry.State == EntityState.Modified)
                {
                    au.UpdatedAtUtc = now;
                    au.UpdatedBy = actor;
                    entry.Property(nameof(IAuditableEntity.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                }
            }

            if (!IsAudited(entry.Entity)) continue;
            var snap = Snapshot(entry, deleting: entry.State == EntityState.Deleted);
            var action = entry.State switch
            {
                EntityState.Added => AuditLogAction.Create,
                EntityState.Deleted => AuditLogAction.Delete,
                _ => IsSoftDeleteFlip(entry) ? AuditLogAction.Delete : AuditLogAction.Update,
            };
            if (action == AuditLogAction.Update && snap.New == "{}") continue;
            audits.Add(NewLog(entry, action, snap.Old, snap.New));
        }

        if (audits.Count > 0) db.Set<AuditLog>().AddRange(audits);
    }

    private static bool IsAudited(object entity) =>
        entity is ApplicationUser || entity.GetType().GetCustomAttribute<AuditedEntityAttribute>() is not null;

    private static bool IsSoftDeleteFlip(EntityEntry e) =>
        e.Entity is ISoftDeletable && e.Property(nameof(ISoftDeletable.IsDeleted)).IsModified && (bool)e.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue!;

    /// <summary>The database composite keys are the hard guarantee; this check fails early with a clear error and also covers tables without a tenant FK.</summary>
    private void GuardTenant(EntityEntry entry)
    {
        Guid? rowTenant;
        switch (entry.Entity)
        {
            case Tenant t:
                if (entry.State != EntityState.Added && !tenant.IsPlatformScope && t.Id != tenant.TenantId)
                    throw new ForbiddenAppException("TENANT_MISMATCH", "Cannot change another tenant.");
                return;
            case ITenantEntity te:
                rowTenant = te.TenantId;
                break;
            case INullableTenantEntity ne:
                rowTenant = ne.TenantId;
                if (rowTenant is null && tenant.IsPlatformScope) return;
                break;
            default:
                return;
        }
        if (rowTenant is null || rowTenant != tenant.TenantId)
            throw new ForbiddenAppException("TENANT_MISMATCH", $"Write to tenant {rowTenant?.ToString() ?? "(none)"} rejected in the current tenant scope.");
    }

    private AuditLog NewLog(EntityEntry entry, AuditLogAction action, string? oldJson, string? newJson)
    {
        var tenantId = (entry.Entity as ITenantEntity)?.TenantId ?? (entry.Entity as INullableTenantEntity)?.TenantId ?? tenant.TenantId;
        var actor = user.UserId;
        var id = entry.Property("Id").CurrentValue;
        return new AuditLog
        {
            TenantId = tenantId,
            UserId = actor,
            Action = action,
            EntityName = entry.Entity.GetType().Name,
            EntityId = id?.ToString(),
            OldValuesJson = oldJson,
            NewValuesJson = newJson,
            IpAddress = user.IpAddress,
            CorrelationId = user.CorrelationId,
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
        };
    }

    private static (string? Old, string? New) Snapshot(EntityEntry entry, bool deleting)
    {
        var oldV = new Dictionary<string, object?>();
        var newV = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            var name = p.Metadata.Name;
            if (name is nameof(IAuditableEntity.UpdatedAtUtc) or nameof(IAuditableEntity.UpdatedBy)) continue;
            var secret = Sensitive.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));
            object? Value(object? v) => secret ? (v is null ? null : "[redacted]") : v;
            switch (entry.State)
            {
                case EntityState.Added:
                    newV[name] = Value(p.CurrentValue);
                    break;
                case EntityState.Deleted:
                    oldV[name] = Value(p.OriginalValue);
                    break;
                case EntityState.Modified when deleting:
                    oldV[name] = Value(p.OriginalValue);
                    break;
                case EntityState.Modified when p.IsModified:
                    oldV[name] = Value(p.OriginalValue);
                    newV[name] = Value(p.CurrentValue);
                    break;
            }
        }
        return (oldV.Count == 0 ? null : JsonSerializer.Serialize(oldV, Json), JsonSerializer.Serialize(newV, Json));
    }
}
