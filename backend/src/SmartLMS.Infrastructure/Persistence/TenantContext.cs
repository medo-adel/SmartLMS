using SmartLMS.Application.Interfaces;

namespace SmartLMS.Infrastructure.Persistence;

/// <summary>Scoped per request (or per test/seed scope). Written by the tenant middleware, read by query filters and the interceptor.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public bool IsPlatformScope { get; private set; }
    public bool ActingViaSupportAccess { get; private set; }

    public void Set(Guid? tenantId, bool isPlatformScope, bool actingViaSupportAccess = false)
    {
        TenantId = tenantId;
        IsPlatformScope = isPlatformScope && tenantId is null;
        ActingViaSupportAccess = actingViaSupportAccess;
    }

    public IDisposable Use(Guid tenantId)
    {
        var previous = (TenantId, IsPlatformScope, ActingViaSupportAccess);
        Set(tenantId, false, previous.ActingViaSupportAccess);
        return new Restore(this, previous);
    }

    private sealed class Restore(TenantContext owner, (Guid? Id, bool Platform, bool Support) previous) : IDisposable
    {
        public void Dispose() => owner.Set(previous.Id, previous.Platform, previous.Support);
    }
}
