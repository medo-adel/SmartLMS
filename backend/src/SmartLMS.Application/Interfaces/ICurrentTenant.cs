namespace SmartLMS.Application.Interfaces;

/// <summary>The tenant a request runs in. Filled by the tenant middleware; every tenant-owned query filter reads it.</summary>
public interface ICurrentTenant
{
    Guid? TenantId { get; }

    /// <summary>True only for platform-level (SuperAdmin) work that has no tenant: tenant management and platform login. It never widens tenant-owned data access.</summary>
    bool IsPlatformScope { get; }

    /// <summary>True when a SuperAdmin is inside a tenant through an active, unexpired SupportAccess membership.</summary>
    bool ActingViaSupportAccess { get; }
}

public interface ITenantContext : ICurrentTenant
{
    void Set(Guid? tenantId, bool isPlatformScope, bool actingViaSupportAccess = false);

    /// <summary>Temporarily switch to a tenant (e.g. while provisioning it). Disposing restores the previous scope.</summary>
    IDisposable Use(Guid tenantId);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? TokenTenantId { get; }
    Guid? DeviceId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    string? IpAddress { get; }
    string? CorrelationId { get; }
}
