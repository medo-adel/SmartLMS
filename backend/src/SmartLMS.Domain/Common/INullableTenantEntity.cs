namespace SmartLMS.Domain.Common;

/// <summary>Platform-aware rows: TenantId is NULL for platform-level data (SuperAdmin identity, platform audit rows).</summary>
public interface INullableTenantEntity
{
    Guid? TenantId { get; set; }
}
