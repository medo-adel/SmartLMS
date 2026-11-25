namespace SmartLMS.Domain.Common;

/// <summary>Changes to entities carrying this attribute are written to AuditLogs by the SaveChanges interceptor.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuditedEntityAttribute : Attribute;
