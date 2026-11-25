namespace SmartLMS.Domain.Common;

/// <summary>Entities that are flagged instead of removed. A global query filter hides them (Phase 2).</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
    Guid? DeletedBy { get; set; }
}
