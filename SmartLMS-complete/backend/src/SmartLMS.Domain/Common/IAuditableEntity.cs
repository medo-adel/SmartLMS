namespace SmartLMS.Domain.Common;

/// <summary>Entities that record who created or last changed them. Filled by the audit interceptor (Phase 2).</summary>
public interface IAuditableEntity
{
    DateTime CreatedAtUtc { get; set; }
    Guid? CreatedBy { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
    Guid? UpdatedBy { get; set; }
}
