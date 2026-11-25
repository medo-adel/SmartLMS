using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Common;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

public sealed class UserService(ApplicationDbContext db, UserManager<ApplicationUser> users, UserProvisioner provisioner, ICurrentUser currentUser, ICurrentTenant tenant, TimeProvider clock) : IUserService
{
    private IQueryable<ApplicationUser> TenantUsers() => db.Users.Where(u => u.TenantId == tenant.TenantId);

    /// <summary>Instructors are restricted to students enrolled in courses they own (see InstructorScope). Admins and SuperAdmin-with-support-access see the whole tenant.</summary>
    private bool Restricted => currentUser.IsInRole(RoleNames.Instructor) && !currentUser.IsInRole(RoleNames.TenantAdmin) && !currentUser.IsInRole(RoleNames.SuperAdmin);

    public async Task<PagedResult<UserDto>> SearchAsync(string? search, string? role, PagedQuery paging, CancellationToken ct)
    {
        var q = TenantUsers().AsNoTracking();
        if (Restricted)
        {
            var me = currentUser.UserId ?? throw new UnauthorizedAppException();
            var mine = db.StudentsOfInstructor(me);
            q = q.Where(u => mine.Contains(u.Id));
        }
        var roleFilter = Restricted ? RoleNames.Student : role;
        if (!string.IsNullOrWhiteSpace(roleFilter))
        {
            q = q.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == roleFilter)));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => u.FullName.Contains(s) || (u.Email != null && u.Email.Contains(s)));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderBy(u => u.FullName).ThenBy(u => u.Id).Skip(paging.Skip).Take(paging.NormalizedPageSize).ToListAsync(ct);
        var ids = page.Select(u => u.Id).ToList();
        var roleRows = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ids.Contains(ur.UserId) select new { ur.UserId, r.Name }).ToListAsync(ct);
        var byUser = roleRows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name!).OrderBy(x => x).ToList());
        var items = page.Select(u => UserProvisioner.ToDto(u, byUser.GetValueOrDefault(u.Id, []))).ToList();
        return new PagedResult<UserDto>(items, paging.NormalizedPage, paging.NormalizedPageSize, total);
    }

    private async Task<ApplicationUser> Find(Guid id, CancellationToken ct) =>
        await TenantUsers().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User not found.");

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await Find(id, ct);
        var dto = await provisioner.ToDtoAsync(u);
        if (Restricted)
        {
            var me = currentUser.UserId ?? throw new UnauthorizedAppException();
            if (!dto.Roles.Contains(RoleNames.Student) || !await db.StudentsOfInstructor(me).AnyAsync(x => x == id, ct)) throw new NotFoundException("User not found.");
        }
        return dto;
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var user = await provisioner.CreateAsync(request.Email, request.FullName, request.Password, request.Roles.Distinct().ToList(), request.PhoneNumber, request.PreferredLanguage, emailConfirmed: true, ct);
        return await provisioner.ToDtoAsync(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var u = await Find(id, ct);
        u.FullName = request.FullName.Trim();
        u.PhoneNumber = request.PhoneNumber;
        u.PreferredLanguage = request.PreferredLanguage;
        var r = await users.UpdateAsync(u);
        if (!r.Succeeded) throw new ValidationAppException("User could not be updated.", r.Errors.Select(e => e.Description));
        return await provisioner.ToDtoAsync(u);
    }

    public async Task<UserDto> SetRolesAsync(Guid id, SetRolesRequest request, CancellationToken ct)
    {
        if (request.Roles.Contains(RoleNames.SuperAdmin))
            throw new ForbiddenAppException("FORBIDDEN", "SuperAdmin is a platform role and cannot be assigned to a tenant user.");
        if (currentUser.UserId == id) throw new ForbiddenAppException("FORBIDDEN", "You cannot change your own roles.");
        var u = await Find(id, ct);
        var target = request.Roles.Distinct().ToList();
        var current = await users.GetRolesAsync(u);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rem = await users.RemoveFromRolesAsync(u, current.Except(target));
        var add = await users.AddToRolesAsync(u, target.Except(current));
        if (!rem.Succeeded || !add.Succeeded) throw new ValidationAppException("Roles could not be updated.", rem.Errors.Concat(add.Errors).Select(e => e.Description));
        await tx.CommitAsync(ct);
        return await provisioner.ToDtoAsync(u);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        if (currentUser.UserId == id) throw new ForbiddenAppException("FORBIDDEN", "You cannot deactivate your own account.");
        var u = await Find(id, ct);
        u.IsActive = false;
        await users.UpdateAsync(u);
        var now = clock.GetUtcNow().UtcDateTime;
        var alive = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in alive) { t.RevokedAtUtc = now; t.RevokedReason = "user_deactivated"; }
        await db.SaveChangesAsync(ct);
    }
}
