using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

public sealed class ProfileService(ApplicationDbContext db, UserManager<ApplicationUser> users, UserProvisioner provisioner, ICurrentUser currentUser, TimeProvider clock) : IProfileService
{
    private async Task<ApplicationUser> Me()
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException();
        return await users.FindByIdAsync(uid.ToString()) ?? throw new NotFoundException("User not found.");
    }

    public async Task<UserDto> GetMeAsync(CancellationToken ct) => await provisioner.ToDtoAsync(await Me());

    public async Task<UserDto> UpdateMeAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await Me();
        user.FullName = request.FullName.Trim();
        if (user.PhoneNumber != request.PhoneNumber) { user.PhoneNumber = request.PhoneNumber; user.PhoneNumberConfirmed = false; }
        user.AvatarUrl = request.AvatarUrl;
        user.PreferredLanguage = request.PreferredLanguage;
        var r = await users.UpdateAsync(user);
        if (!r.Succeeded) throw new ValidationAppException("Profile could not be updated.", r.Errors.Select(e => e.Description));
        return await provisioner.ToDtoAsync(user);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await Me();
        var r = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!r.Succeeded) throw new ValidationAppException("Password could not be changed.", r.Errors.Select(e => e.Description));
        var now = clock.GetUtcNow().UtcDateTime;
        var others = await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null && t.DeviceId != currentUser.DeviceId).ToListAsync(ct);
        foreach (var t in others) { t.RevokedAtUtc = now; t.RevokedReason = "password_changed"; }
        await db.SaveChangesAsync(ct);
    }
}
