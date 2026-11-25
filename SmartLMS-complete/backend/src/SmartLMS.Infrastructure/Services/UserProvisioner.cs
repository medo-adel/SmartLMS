using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

/// <summary>Single place that creates identities, so every user always gets the right UserName, a Home membership and roles in one transaction.</summary>
public sealed class UserProvisioner(ApplicationDbContext db, UserManager<ApplicationUser> users, ICurrentTenant tenant)
{
    public const string PlatformPrefix = "platform";

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public async Task<string> UserNameFor(string email, CancellationToken ct)
    {
        if (tenant.TenantId is null)
        {
            return $"{PlatformPrefix}:{Normalize(email)}";
        }
        var slug = await db.Tenants.AsNoTracking().Where(t => t.Id == tenant.TenantId).Select(t => t.Slug).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Tenant not found.");
        return $"{slug}:{Normalize(email)}";
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var name = await UserNameFor(email, ct);
        return await users.FindByNameAsync(name);
    }

    public async Task<ApplicationUser> CreateAsync(string email, string fullName, string password, IReadOnlyCollection<string> roles, string? phone,
        string? language, bool emailConfirmed, CancellationToken ct)
    {
        var platform = tenant.TenantId is null;
        if (platform && !roles.All(r => r == RoleNames.SuperAdmin)) throw new ValidationAppException("Platform users can only be SuperAdmin.");
        if (!platform && roles.Contains(RoleNames.SuperAdmin)) throw new ValidationAppException("SuperAdmin is a platform role and cannot be assigned inside a tenant.");
        if (!platform && roles.Count == 0) throw new ValidationAppException("At least one role is required.");

        var userName = await UserNameFor(email, ct);
        if (await users.FindByNameAsync(userName) is not null) throw new ConflictAppException("EMAIL_IN_USE", "An account with this email already exists.");

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            UserName = userName,
            Email = Normalize(email),
            EmailConfirmed = emailConfirmed,
            FullName = fullName.Trim(),
            PhoneNumber = phone,
            PreferredLanguage = language ?? "en",
            IsActive = true,
            LockoutEnabled = true,
        };

        var ownTx = db.Database.CurrentTransaction is null;
        await using var tx = ownTx ? await db.Database.BeginTransactionAsync(ct) : null;
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) throw new ValidationAppException("User could not be created.", created.Errors.Select(e => e.Description));

        if (!platform)
        {
            db.UserTenantMemberships.Add(new UserTenantMembership { TenantId = tenant.TenantId!.Value, UserId = user.Id, Kind = UserTenantMembershipKind.Home, GrantedBy = null });
            await db.SaveChangesAsync(ct);
        }
        var roleResult = await users.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded) throw new ValidationAppException("Roles could not be assigned.", roleResult.Errors.Select(e => e.Description));
        if (tx is not null) await tx.CommitAsync(ct);
        return user;
    }

    public static UserDto ToDto(ApplicationUser u, IReadOnlyList<string> roles) =>
        new(u.Id, u.TenantId, u.FullName, u.Email, u.PhoneNumber, u.PreferredLanguage, u.IsActive, u.EmailConfirmed, roles, u.LastLoginAtUtc);

    public async Task<UserDto> ToDtoAsync(ApplicationUser u) => ToDto(u, (await users.GetRolesAsync(u)).ToList());
}
