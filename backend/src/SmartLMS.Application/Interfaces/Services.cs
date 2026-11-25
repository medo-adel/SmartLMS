using SmartLMS.Application.Common;
using SmartLMS.Application.Dtos;
using SmartLMS.Domain.Entities;

namespace SmartLMS.Application.Interfaces;

public interface IAuthService
{
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct);
    Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct);
    Task RequestOtpAsync(OtpRequest request, CancellationToken ct);
    Task<bool> VerifyOtpAsync(OtpVerifyRequest request, CancellationToken ct);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    Task EnableBiometricAsync(CancellationToken ct);
}

public interface IProfileService
{
    Task<UserDto> GetMeAsync(CancellationToken ct);
    Task<UserDto> UpdateMeAsync(UpdateProfileRequest request, CancellationToken ct);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
}

public interface IDeviceService
{
    Task<IReadOnlyList<UserDeviceDto>> ListForUserAsync(Guid userId, CancellationToken ct);
    Task RemoveOwnDeviceAsync(Guid deviceId, CancellationToken ct);
    Task<int> ResetUserDevicesAsync(Guid userId, string? reason, CancellationToken ct);

    /// <summary>Registers (or re-activates) the device for a user. Students are capped per Tenants.MaxMobileDevices / MaxWebDevices; other roles are not.</summary>
    Task<UserDevice> BindAsync(Guid userId, Guid? tenantId, bool enforceCap, DeviceInfo info, CancellationToken ct);
}

public interface IUserService
{
    Task<PagedResult<UserDto>> SearchAsync(string? search, string? role, PagedQuery paging, CancellationToken ct);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
    Task<UserDto> SetRolesAsync(Guid id, SetRolesRequest request, CancellationToken ct);
    Task DeactivateAsync(Guid id, CancellationToken ct);
}

public interface ITenantService
{
    Task<BrandingDto> GetBrandingAsync(CancellationToken ct);
    Task<PagedResult<TenantDto>> ListAsync(PagedQuery paging, CancellationToken ct);
    Task<TenantDto> GetAsync(Guid id, CancellationToken ct);
    Task<TenantDto> CreateAsync(CreateTenantRequest request, CancellationToken ct);
    Task<TenantDto> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken ct);
    Task<TenantDto> UpdateSettingsAsync(TenantSettingsRequest request, CancellationToken ct);
    Task<IReadOnlyList<FeatureFlagDto>> ListFlagsAsync(CancellationToken ct);
    Task<FeatureFlagDto> SetFlagAsync(string key, bool enabled, CancellationToken ct);
    Task<SupportAccessDto> GrantSupportAccessAsync(Guid tenantId, int? minutes, CancellationToken ct);
    Task RevokeSupportAccessAsync(Guid tenantId, CancellationToken ct);
}

public interface ITokenService
{
    /// <summary>Creates a signed access token. The token carries sub, tid (absent for SuperAdmin), roles, did, email and name.</summary>
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(Guid userId, Guid? tenantId, string email, string fullName, IEnumerable<string> roles, Guid? deviceId);
    string NewRefreshToken();
    string Hash(string value);
}

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends one email. The default implementation sends nothing; SMTP is used only when Email:Enabled is true and the server is configured.</summary>
public interface IEmailTransport
{
    bool IsEnabled { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public interface ILogoService
{
    /// <summary>Stores a PNG, JPEG or WebP logo (max 2 MB, type decided from the file bytes) for the current tenant and sets Tenant.LogoUrl.</summary>
    Task<TenantDto> UploadAsync(Stream content, long length, CancellationToken ct);
    Task<(Stream Content, string ContentType)> OpenAsync(CancellationToken ct);
}

public interface IOtpSender
{
    Task SendAsync(string destination, string purpose, string code, CancellationToken ct);
}
