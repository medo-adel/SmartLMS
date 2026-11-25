using FluentValidation;
using SmartLMS.Application.Dtos;
using SmartLMS.Domain.Enums;

namespace SmartLMS.Application;

internal static class Rules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rb) =>
        rb.NotEmpty().MinimumLength(10).MaximumLength(128)
          .Matches("[A-Z]").WithMessage("Password needs an uppercase letter.")
          .Matches("[a-z]").WithMessage("Password needs a lowercase letter.")
          .Matches("[0-9]").WithMessage("Password needs a digit.");

    public static IRuleBuilderOptions<T, string> EmailRule<T>(this IRuleBuilder<T, string> rb) =>
        rb.NotEmpty().MaximumLength(200).EmailAddress();

    public static readonly string[] Purposes = ["EmailVerification", "PhoneVerification", "PasswordReset", "Login"];
}

public sealed class DeviceInfoValidator : AbstractValidator<DeviceInfo>
{
    public DeviceInfoValidator()
    {
        RuleFor(x => x.Fingerprint).NotEmpty().MinimumLength(16).MaximumLength(512);
        RuleFor(x => x.DeviceType).Must(v => v is "Mobile" or "Web").WithMessage("DeviceType must be Mobile or Web.");
        RuleFor(x => x.Platform).MaximumLength(30);
        RuleFor(x => x.DeviceName).MaximumLength(200);
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailRule();
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.Role).Must(r => r is null or RoleNames.Student or RoleNames.Parent).WithMessage("Public registration allows Student or Parent only.");
        RuleFor(x => x.PreferredLanguage).MaximumLength(10);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).EmailRule();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Device).NotNull().SetValidator(new DeviceInfoValidator());
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(512);
    }
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Email).EmailRule();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
    }
}

public sealed class OtpRequestValidator : AbstractValidator<OtpRequest>
{
    public OtpRequestValidator()
    {
        RuleFor(x => x.Destination).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Purpose).Must(p => Rules.Purposes.Contains(p)).WithMessage("Unknown OTP purpose.");
    }
}

public sealed class OtpVerifyRequestValidator : AbstractValidator<OtpVerifyRequest>
{
    public OtpVerifyRequestValidator()
    {
        RuleFor(x => x.Destination).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Purpose).Must(p => Rules.Purposes.Contains(p)).WithMessage("Unknown OTP purpose.");
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).EmailRule();
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).EmailRule();
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$");
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword().NotEqual(x => x.CurrentPassword).WithMessage("New password must differ from the current one.");
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.AvatarUrl).MaximumLength(500);
        RuleFor(x => x.PreferredLanguage).NotEmpty().MaximumLength(10);
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailRule();
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Roles).NotEmpty().Must(r => r.All(RoleNames.All.Contains)).WithMessage("Unknown role.");
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.PreferredLanguage).NotEmpty().MaximumLength(10);
    }
}

public sealed class SetRolesRequestValidator : AbstractValidator<SetRolesRequest>
{
    public SetRolesRequestValidator() =>
        RuleFor(x => x.Roles).NotEmpty().Must(r => r.All(RoleNames.All.Contains)).WithMessage("Unknown role.");
}

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100).Matches("^[a-z0-9]+(-[a-z0-9]+)*$").WithMessage("Slug must be lowercase letters, digits and hyphens.");
        RuleFor(x => x.Subdomain).MaximumLength(100).Matches("^[a-z0-9]+(-[a-z0-9]+)*$").When(x => x.Subdomain is not null);
        RuleFor(x => x.AdminFullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminEmail).EmailRule();
        RuleFor(x => x.AdminPassword).StrongPassword();
        RuleFor(x => x.MaxMobileDevices).InclusiveBetween(1, 10);
        RuleFor(x => x.MaxWebDevices).InclusiveBetween(1, 10);
        RuleFor(x => x.Currency).Length(3);
    }
}

public sealed class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Subdomain).MaximumLength(100).Matches("^[a-z0-9]+(-[a-z0-9]+)*$").When(x => x.Subdomain is not null);
        RuleFor(x => x.MaxMobileDevices).InclusiveBetween(1, 10);
        RuleFor(x => x.MaxWebDevices).InclusiveBetween(1, 10);
        RuleFor(x => x.DefaultLanguage).NotEmpty().MaximumLength(10);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Currency).Length(3);
    }
}

public sealed class TenantSettingsRequestValidator : AbstractValidator<TenantSettingsRequest>
{
    private const string Color = "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$";
    public TenantSettingsRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PrimaryColor).Matches(Color).When(x => x.PrimaryColor is not null);
        RuleFor(x => x.SecondaryColor).Matches(Color).When(x => x.SecondaryColor is not null);
        RuleFor(x => x.AccentColor).Matches(Color).When(x => x.AccentColor is not null);
        RuleFor(x => x.DefaultLanguage).NotEmpty().MaximumLength(10);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Currency).Length(3);
    }
}

public sealed class SupportAccessRequestValidator : AbstractValidator<SupportAccessRequest>
{
    public SupportAccessRequestValidator() =>
        RuleFor(x => x.Minutes).InclusiveBetween(1, 480).When(x => x.Minutes is not null);
}

public sealed class ResetDevicesRequestValidator : AbstractValidator<ResetDevicesRequest>
{
    public ResetDevicesRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}
