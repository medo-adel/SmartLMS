namespace SmartLMS.Domain.Common;

public abstract class BaseEntity
{
    /// <summary>Generated client-side (time-ordered GUID v7) so audit rows can reference new entities before SaveChanges.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
}
