namespace SmartLMS.Domain.Enums;

/// <summary>Role names used by ASP.NET Core Identity and [Authorize(Roles = ...)].</summary>
public static class RoleNames
{
    public const string SuperAdmin = nameof(SuperAdmin);
    public const string TenantAdmin = nameof(TenantAdmin);
    public const string Instructor = nameof(Instructor);
    public const string TeachingAssistant = nameof(TeachingAssistant);
    public const string Student = nameof(Student);
    public const string Parent = nameof(Parent);

    public static readonly IReadOnlyList<string> All =
        [SuperAdmin, TenantAdmin, Instructor, TeachingAssistant, Student, Parent];
}
