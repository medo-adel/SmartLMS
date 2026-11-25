using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

/// <summary>
/// Who may act on a course. Author = TenantAdmin, the owner, or a CoInstructor (also needs the Instructor role).
/// Grader = author or a TeachingAssistant (also needs the TeachingAssistant role) of that course. Staff rows are tenant-scoped by the query filter.
/// </summary>
public sealed class CourseAccess(ApplicationDbContext db, ICurrentUser me)
{
    private bool Admin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    public bool IsOwnerOrAdmin(Course c) => Admin || (me.UserId is { } u && c.OwnerInstructorId == u);

    public async Task<bool> IsAuthorAsync(Course c, CancellationToken ct)
    {
        if (IsOwnerOrAdmin(c)) return true;
        if (me.UserId is not { } u || !me.IsInRole(RoleNames.Instructor)) return false;
        return await db.CourseStaff.AnyAsync(s => s.CourseId == c.Id && s.UserId == u && s.StaffRole == CourseStaffStaffRole.CoInstructor, ct);
    }
    public async Task<bool> IsGraderAsync(Course c, CancellationToken ct)
    {
        if (await IsAuthorAsync(c, ct)) return true;
        if (me.UserId is not { } u || !me.IsInRole(RoleNames.TeachingAssistant)) return false;
        return await db.CourseStaff.AnyAsync(s => s.CourseId == c.Id && s.UserId == u && s.StaffRole == CourseStaffStaffRole.TeachingAssistant, ct);
    }
    /// <summary>Courses the caller owns or co-instructs (and, with graders, assists).</summary>
    public IQueryable<Guid> CourseIds(bool graders)
    {
        var u = me.UserId ?? Guid.Empty; var inst = me.IsInRole(RoleNames.Instructor); var ta = graders && me.IsInRole(RoleNames.TeachingAssistant);
        return db.Courses.Where(c => c.OwnerInstructorId == u
            || db.CourseStaff.Any(s => s.CourseId == c.Id && s.UserId == u && ((inst && s.StaffRole == CourseStaffStaffRole.CoInstructor) || (ta && s.StaffRole == CourseStaffStaffRole.TeachingAssistant)))).Select(c => c.Id);
    }
}
