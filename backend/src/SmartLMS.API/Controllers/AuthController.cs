using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartLMS.Application.Common;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Exceptions;

namespace SmartLMS.API.Controllers;

[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IAuthService auth, IEmailTransport mail) : ApiControllerBase
{
    /// <summary>Tells the UI how verification codes actually leave this server, so it never promises mailbox delivery it cannot make. Exposes only the mode word, never hosts, paths or accounts.</summary>
    [HttpGet("mail-delivery"), AllowAnonymous]
    public ActionResult<ApiResponse<object>> MailDelivery() => Success<object>(new { mode = mail.DeliveryMode });

    [HttpPost("register"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<UserDto>>> Register(RegisterRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<UserDto>.Ok(await auth.RegisterAsync(request, ct), "Registered. Verify your email with the code we sent."));

    [HttpPost("login"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Login(LoginRequest request, CancellationToken ct) => Success(await auth.LoginAsync(request, ct));

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Refresh(RefreshRequest request, CancellationToken ct) => Success(await auth.RefreshAsync(request, ct));

    [HttpPost("logout"), Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return Done("Signed out.");
    }

    [HttpPost("verify-email"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        await auth.VerifyEmailAsync(request, ct);
        return Done("Email verified.");
    }

    [HttpPost("otp/request"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> OtpRequest(OtpRequest request, CancellationToken ct)
    {
        await auth.RequestOtpAsync(request, ct);
        return Done("If the destination is registered, a code was sent.");
    }

    [HttpPost("otp/verify"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> OtpVerify(OtpVerifyRequest request, CancellationToken ct)
    {
        if (!await auth.VerifyOtpAsync(request, ct)) throw new ValidationAppException("The code is invalid or expired.");
        return Done("Code verified.");
    }

    [HttpPost("forgot-password"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> Forgot(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request, ct);
        return Done("If the account exists, a reset code was sent.");
    }

    [HttpPost("reset-password"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> Reset(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return Done("Password updated. Sign in again.");
    }

    [HttpPost("social/{provider}"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Social(string provider, SocialLoginRequest request, CancellationToken ct) => Success(await auth.SocialLoginAsync(provider, request, ct));

    /// <summary>Which social providers are configured (client ids the login page needs). Never exposes server secrets.</summary>
    [HttpGet("external-providers"), AllowAnonymous]
    public ActionResult<ApiResponse<object>> ExternalProviders([FromServices] IEnumerable<IExternalSignInVerifier> verifiers) =>
        Success((object)new { providers = verifiers.Where(v => v.IsConfigured).Select(v => new { provider = v.Provider, clientId = v.ClientId }) });

    [HttpPost("biometric/enable"), Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> Biometric(CancellationToken ct)
    {
        await auth.EnableBiometricAsync(ct);
        return Done("Biometric unlock enabled.");
    }
}
