using Microsoft.EntityFrameworkCore;
using SmartLMS.Domain.Enums;

namespace SmartLMS.Infrastructure.Persistence;

public static class InstructorScope
{
    /// <summary>
    /// Ids of students an instructor may see: users holding a non-revoked enrollment in a course the instructor owns.
    /// Both tables are tenant-filtered, so this can never cross tenants. Until Phase 3 creates enrollments the result is empty,
    /// which means instructors see no users. That is intended: no relationship, no access.
    /// </summary>
    public static IQueryable<Guid> StudentsOfInstructor(this ApplicationDbContext db, Guid instructorId) =>
        from e in db.Enrollments
        join c in db.Courses on e.CourseId equals c.Id
        where c.OwnerInstructorId == instructorId && e.Status != EnrollmentStatus.Revoked
        select e.UserId;
}
