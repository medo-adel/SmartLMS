namespace SmartLMS.Application.Dtos;

public sealed record DeviceInfo(string Fingerprint, string DeviceType, string? Platform = null, string? DeviceName = null);

public sealed record RegisterRequest(string FullName, string Email, string Password, string? PhoneNumber = null, string? Role = null, string? PreferredLanguage = null);
public sealed record LoginRequest(string Email, string Password, DeviceInfo Device);
public sealed record RefreshRequest(string RefreshToken, string DeviceFingerprint);
public sealed record LogoutRequest(string RefreshToken);
public sealed record VerifyEmailRequest(string Email, string Code);
public sealed record OtpRequest(string Destination, string Purpose);
public sealed record OtpVerifyRequest(string Destination, string Purpose, string Code);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record UpdateProfileRequest(string FullName, string? PhoneNumber, string? AvatarUrl, string PreferredLanguage);

public sealed record UserDto(Guid Id, Guid? TenantId, string FullName, string? Email, string? PhoneNumber, string PreferredLanguage,
    bool IsActive, bool EmailConfirmed, IReadOnlyList<string> Roles, DateTime? LastLoginAtUtc);

public sealed record TokenResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, UserDto User);

public sealed record CreateUserRequest(string FullName, string Email, string Password, IReadOnlyList<string> Roles, string? PhoneNumber = null, string? PreferredLanguage = null);
public sealed record UpdateUserRequest(string FullName, string? PhoneNumber, string PreferredLanguage);
public sealed record SetRolesRequest(IReadOnlyList<string> Roles);
public sealed record ResetDevicesRequest(string? Reason);

public sealed record UserDeviceDto(Guid Id, string DeviceType, string? Platform, string? DeviceName, DateTime FirstSeenAtUtc, DateTime LastSeenAtUtc, bool IsActive);

public sealed record TenantDto(Guid Id, string Name, string Slug, string? Subdomain, string? LogoUrl, string? PrimaryColor, string? SecondaryColor,
    string? AccentColor, string DefaultLanguage, string TimeZoneId, string Currency, int MaxMobileDevices, int MaxWebDevices, bool IsActive);

public sealed record CreateTenantRequest(string Name, string Slug, string? Subdomain, string AdminFullName, string AdminEmail, string AdminPassword,
    int MaxMobileDevices = 1, int MaxWebDevices = 1, string DefaultLanguage = "en", string TimeZoneId = "Africa/Cairo", string Currency = "EGP");

public sealed record UpdateTenantRequest(string Name, string? Subdomain, bool IsActive, int MaxMobileDevices, int MaxWebDevices,
    string DefaultLanguage, string TimeZoneId, string Currency);

public sealed record TenantSettingsRequest(string Name, string? PrimaryColor, string? SecondaryColor, string? AccentColor,
    string DefaultLanguage, string TimeZoneId, string Currency);

public sealed record FeatureFlagDto(string Key, bool IsEnabled);
public sealed record SetFeatureFlagRequest(bool IsEnabled);
public sealed record BrandingDto(string Name, string Slug, string? LogoUrl, string? PrimaryColor, string? SecondaryColor, string? AccentColor,
    string DefaultLanguage, IReadOnlyList<FeatureFlagDto> FeatureFlags);

public sealed record SupportAccessRequest(int? Minutes);
public sealed record SupportAccessDto(Guid TenantId, DateTime ExpiresAtUtc);
