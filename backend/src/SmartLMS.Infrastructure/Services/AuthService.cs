using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

public sealed class AuthService(
    ApplicationDbContext db, UserManager<ApplicationUser> users, UserProvisioner provisioner, ITokenService tokens, IDeviceService devices,
    IOtpSender otpSender, ICurrentTenant tenant, ICurrentUser currentUser, IOptions<JwtOptions> jwt, IOptions<OtpOptions> otp, TimeProvider clock, TransactionalMailer mail, IEnumerable<IExternalSignInVerifier> verifiers) : IAuthService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ValidationAppException("Registration needs a tenant (X-Tenant-Id).");
        var role = request.Role ?? RoleNames.Student;
        if (role == RoleNames.Parent)
        {
            var enabled = await db.TenantFeatureFlags.AsNoTracking().AnyAsync(f => f.FeatureKey == "parent_registration" && f.IsEnabled, ct);
            if (!enabled) throw new ForbiddenAppException("PARENT_REGISTRATION_DISABLED", "Parent registration is not enabled for this tenant.");
        }
        var user = await provisioner.CreateAsync(request.Email, request.FullName, request.Password, [role], request.PhoneNumber, request.PreferredLanguage, emailConfirmed: false, ct);
        await IssueOtpAsync(user, user.Email!, OtpCodePurpose.EmailVerification, ct);
        return await provisioner.ToDtoAsync(user);
    }

    /// <summary>
    /// Google sign-in: verifies the provider's ID token, then links the external identity to the tenant account
    /// with the same email or creates a Student account (email already verified by the provider, so EmailConfirmed).
    /// </summary>
    public async Task<TokenResponse> SocialLoginAsync(string provider, SocialLoginRequest request, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ValidationAppException("A tenant is required (X-Tenant-Id).");
        var verifier = verifiers.FirstOrDefault(v => string.Equals(v.Provider, provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new AppException(501, "NOT_IMPLEMENTED", $"Social login ({provider}) is not configured in this build.");
        if (!verifier.IsConfigured) throw new AppException(501, "NOT_IMPLEMENTED", $"Social login ({provider}) is not configured in this build.");
        var ext = await verifier.VerifyAsync(request.IdToken, ct);
        if (!ext.EmailVerified) throw new ForbiddenAppException("EMAIL_NOT_VERIFIED", "The provider account has no verified email.");

        // External logins are globally unique per (provider, providerKey): one Google account maps to one LMS
        // account across every tenant. Sign in by the linked identity first, then fall back to email matching.
        var user = await users.FindByLoginAsync(ext.Provider, ext.ProviderKey);
        if (user is null)
        {
            var linkedElsewhere = await db.Set<IdentityUserLogin<Guid>>().IgnoreQueryFilters()
                .AnyAsync(l => l.LoginProvider == ext.Provider && l.ProviderKey == ext.ProviderKey, ct);
            if (linkedElsewhere)
                throw new ConflictAppException("EXTERNAL_IDENTITY_IN_USE", "This provider account is linked to an account in another academy.");
            user = await provisioner.FindByEmailAsync(ext.Email, ct);
            if (user is null)
            {
                user = await provisioner.CreateAsync(ext.Email, ext.FullName, "Ext!" + Guid.NewGuid().ToString("N"), [RoleNames.Student], null, null, emailConfirmed: true, ct);
                await users.AddLoginAsync(user, new UserLoginInfo(ext.Provider, ext.ProviderKey, ext.Provider));
            }
            else
            {
                await users.AddLoginAsync(user, new UserLoginInfo(ext.Provider, ext.ProviderKey, ext.Provider));
            }
        }
        if (user.IsDeleted) throw new UnauthorizedAppException("INVALID_CREDENTIALS", "Invalid email or password.");
        if (!user.IsActive) throw new ForbiddenAppException("ACCOUNT_DISABLED", "This account is deactivated.");
        db.Entry(user).State = EntityState.Detached;
        var roles = (await users.GetRolesAsync(user)).ToList();
        var device = await devices.BindAsync(user.Id, user.TenantId, enforceCap: roles.Contains(RoleNames.Student), request.Device, ct);
        var loginAt = Now;
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAtUtc, loginAt), ct);
        db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.Id, Action = AuditLogAction.Login, EntityName = "User", EntityId = user.Id.ToString(), IpAddress = currentUser.IpAddress, CorrelationId = currentUser.CorrelationId, OccurredAtUtc = Now });
        return await IssueAsync(user, roles, device, ct);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        if (tenant.TenantId is null && !tenant.IsPlatformScope) throw new ValidationAppException("A tenant is required (X-Tenant-Id).");
        var user = await provisioner.FindByEmailAsync(request.Email, ct);
        if (user is null || user.IsDeleted)
        {
            throw new UnauthorizedAppException("INVALID_CREDENTIALS", "Invalid email or password.");
        }
        if (await users.IsLockedOutAsync(user)) throw new ForbiddenAppException("ACCOUNT_LOCKED", "Too many failed attempts. Try again later.");
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new UnauthorizedAppException("INVALID_CREDENTIALS", "Invalid email or password.");
        }
        if (!user.IsActive) throw new ForbiddenAppException("ACCOUNT_DISABLED", "This account is deactivated.");
        if (!user.EmailConfirmed) throw new ForbiddenAppException("EMAIL_NOT_CONFIRMED", "Verify your email before signing in.");
        await users.ResetAccessFailedCountAsync(user);
        // Parallel logins for one account each try to update this row. A losing Identity update leaves the tracked user dirty with a stale
        // concurrency stamp, and the next SaveChanges (token issue) would throw. The user is only read after this point, so stop tracking it.
        db.Entry(user).State = EntityState.Detached;

        var roles = (await users.GetRolesAsync(user)).ToList();
        var device = await devices.BindAsync(user.Id, user.TenantId, enforceCap: roles.Contains(RoleNames.Student), request.Device, ct);

        var loginAt = Now;
        user.LastLoginAtUtc = loginAt;
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAtUtc, loginAt), ct);
        db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.Id, Action = AuditLogAction.Login, EntityName = "User", EntityId = user.Id.ToString(), IpAddress = currentUser.IpAddress, CorrelationId = currentUser.CorrelationId, OccurredAtUtc = Now });
        return await IssueAsync(user, roles, device, ct);
    }

    private async Task<TokenResponse> IssueAsync(ApplicationUser user, List<string> roles, UserDevice device, CancellationToken ct)
    {
        var (access, accessExp) = tokens.CreateAccessToken(user.Id, user.TenantId, user.Email ?? string.Empty, user.FullName, roles, device.Id);
        var refresh = tokens.NewRefreshToken();
        var refreshExp = Now.AddDays(jwt.Value.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken { TenantId = user.TenantId, UserId = user.Id, DeviceId = device.Id, TokenHash = tokens.Hash(refresh), ExpiresAtUtc = refreshExp, CreatedByIp = currentUser.IpAddress });
        await db.SaveChangesAsync(ct);
        return new TokenResponse(access, accessExp, refresh, refreshExp, UserProvisioner.ToDto(user, roles));
    }

    private async Task<Exception> ReuseDetectedAsync(RefreshToken stored, CancellationToken ct)
    {
        // A token that was already used or revoked came back. Treat it as copied and revoke everything still alive on that device.
        var alive = await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.DeviceId == stored.DeviceId && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in alive) { t.RevokedAtUtc = Now; t.RevokedReason = "reuse_detected"; }
        await db.SaveChangesAsync(ct);
        return new UnauthorizedAppException("REFRESH_TOKEN_REUSED", "Refresh token was already used. Sign in again.");
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokens.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw new UnauthorizedAppException("INVALID_REFRESH_TOKEN", "Refresh token is not valid.");

        if (stored.RevokedAtUtc is not null) throw await ReuseDetectedAsync(stored, ct);
        if (stored.ExpiresAtUtc <= Now) throw new UnauthorizedAppException("INVALID_REFRESH_TOKEN", "Refresh token expired.");

        var device = await db.UserDevices.FirstOrDefaultAsync(d => d.Id == stored.DeviceId && d.UserId == stored.UserId && d.IsActive, ct);
        if (device is null || device.DeviceFingerprintHash != tokens.Hash(request.DeviceFingerprint))
            throw new ForbiddenAppException("DEVICE_MISMATCH", "This refresh token belongs to a different device.");

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null || user.IsDeleted || !user.IsActive) throw new UnauthorizedAppException("INVALID_REFRESH_TOKEN", "Account is not active.");

        // Atomic claim: exactly one concurrent request can flip RevokedAtUtc from NULL. Every other request with the same token is a reuse.
        var now = Now;
        var claimed = await db.RefreshTokens.Where(t => t.Id == stored.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now).SetProperty(t => t.RevokedReason, "rotated"), ct);
        if (claimed == 0) throw await ReuseDetectedAsync(stored, ct);

        var roles = (await users.GetRolesAsync(user)).ToList();
        var response = await IssueAsync(user, roles, device, ct);
        var next = tokens.Hash(response.RefreshToken);
        await db.RefreshTokens.Where(t => t.Id == stored.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedByTokenHash, next), ct);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException();
        var hash = tokens.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == uid && t.RevokedAtUtc == null, ct);
        if (stored is null) return;
        stored.RevokedAtUtc = Now;
        stored.RevokedReason = "logout";
        await db.SaveChangesAsync(ct);
    }

    // ---- OTP ---------------------------------------------------------------------------------------------------------------

    private string OtpHash(string destination, OtpCodePurpose purpose, string code) => tokens.Hash($"{purpose}|{destination.Trim().ToLowerInvariant()}|{code}");

    private async Task IssueOtpAsync(ApplicationUser? user, string destination, OtpCodePurpose purpose, CancellationToken ct)
    {
        var dest = destination.Trim().ToLowerInvariant();
        var old = await db.OtpCodes.Where(o => o.Destination == dest && o.Purpose == purpose && o.ConsumedAtUtc == null).ToListAsync(ct);
        foreach (var o in old) o.ConsumedAtUtc = Now;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.OtpCodes.Add(new OtpCode { TenantId = tenant.TenantId, UserId = user?.Id, Destination = dest, Purpose = purpose, CodeHash = OtpHash(dest, purpose, code), ExpiresAtUtc = Now.AddMinutes(otp.Value.ExpiryMinutes) });
        await db.SaveChangesAsync(ct);
        await otpSender.SendAsync(dest, purpose.ToString(), code, ct);
    }

    private async Task<OtpCode?> ConsumeOtpAsync(string destination, OtpCodePurpose purpose, string code, CancellationToken ct)
    {
        var dest = destination.Trim().ToLowerInvariant();
        var row = await db.OtpCodes.Where(o => o.Destination == dest && o.Purpose == purpose && o.ConsumedAtUtc == null && o.ExpiresAtUtc > Now)
            .OrderByDescending(o => o.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (row is null || row.AttemptCount >= otp.Value.MaxAttempts) return null;
        var expected = Encoding.UTF8.GetBytes(row.CodeHash);
        var actual = Encoding.UTF8.GetBytes(OtpHash(dest, purpose, code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            row.AttemptCount++;
            await db.SaveChangesAsync(ct);
            return null;
        }
        row.ConsumedAtUtc = Now;
        await db.SaveChangesAsync(ct);
        return row;
    }

    private static OtpCodePurpose ParsePurpose(string p) => Enum.Parse<OtpCodePurpose>(p, ignoreCase: true);

    public async Task RequestOtpAsync(OtpRequest request, CancellationToken ct)
    {
        // Same response whether or not the destination belongs to an account, so this cannot be used to list accounts.
        var purpose = ParsePurpose(request.Purpose);
        // No SMS provider exists. Fail visibly and issue nothing, so nobody believes a code was sent or that phone verification can complete.
        if (purpose is OtpCodePurpose.PhoneVerification)
            throw new AppException(501, "SMS_NOT_CONFIGURED", "Phone verification is not available: no SMS provider is configured. No code was sent.");
        var user = purpose is OtpCodePurpose.PhoneVerification
            ? await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.Destination, ct)
            : await provisioner.FindByEmailAsync(request.Destination, ct);
        if (user is null || user.IsDeleted || !user.IsActive) return;
        await IssueOtpAsync(user, request.Destination, purpose, ct);
    }

    public async Task<bool> VerifyOtpAsync(OtpVerifyRequest request, CancellationToken ct)
    {
        var purpose = ParsePurpose(request.Purpose);
        if (purpose == OtpCodePurpose.PasswordReset) throw new ValidationAppException("Use reset-password to redeem a password reset code.");
        var row = await ConsumeOtpAsync(request.Destination, purpose, request.Code, ct);
        if (row is null) return false;
        if (row.UserId is not null)
        {
            var user = await users.FindByIdAsync(row.UserId.ToString()!);
            if (user is not null)
            {
                if (purpose == OtpCodePurpose.EmailVerification) user.EmailConfirmed = true;
                if (purpose == OtpCodePurpose.PhoneVerification) user.PhoneNumberConfirmed = true;
                await users.UpdateAsync(user);
            }
        }
        return true;
    }

    public async Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        if (!await VerifyOtpAsync(new OtpVerifyRequest(request.Email, nameof(OtpCodePurpose.EmailVerification), request.Code), ct))
            throw new ValidationAppException("The code is invalid or expired.");
        var user = await provisioner.FindByEmailAsync(request.Email, ct);
        if (user is not null) await mail.WelcomeAsync(user.Id, ct); // best-effort, never throws
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var user = await provisioner.FindByEmailAsync(request.Email, ct);
        if (user is not null && !user.IsDeleted && user.IsActive) await IssueOtpAsync(user, request.Email, OtpCodePurpose.PasswordReset, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var row = await ConsumeOtpAsync(request.Email, OtpCodePurpose.PasswordReset, request.Code, ct);
        var user = row?.UserId is null ? null : await users.FindByIdAsync(row.UserId.ToString()!);
        if (user is null) throw new ValidationAppException("The code is invalid or expired.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded) throw new ValidationAppException("Password could not be reset.", result.Errors.Select(e => e.Description));
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        var alive = await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in alive) { t.RevokedAtUtc = Now; t.RevokedReason = "password_reset"; }
        await db.SaveChangesAsync(ct);
    }

    public async Task EnableBiometricAsync(CancellationToken ct)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException();
        var user = await users.FindByIdAsync(uid.ToString()) ?? throw new NotFoundException("User not found.");
        user.BiometricLoginEnabled = true;
        await users.UpdateAsync(user);
    }
}
