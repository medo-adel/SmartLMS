using Microsoft.AspNetCore.Identity;
using SmartLMS.Domain.Common;

namespace SmartLMS.Infrastructure.Identity;

/// <summary>
/// Global identity row. TenantId is NULL only for platform users (SuperAdmin). UserName is "&lt;tenantSlug&gt;:&lt;email&gt;" ("platform:&lt;email&gt;" for SuperAdmin),
/// so the same email can exist in two tenants while the unique index stays (TenantId, NormalizedUserName).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>, INullableTenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid? TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
    public bool BiometricLoginEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { Id = Guid.CreateVersion7(); }
}
