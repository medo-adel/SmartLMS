namespace SmartLMS.Domain.Common;

/// <summary>Entities owned by one tenant. A global query filter scopes reads by TenantId (Phase 2).</summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
