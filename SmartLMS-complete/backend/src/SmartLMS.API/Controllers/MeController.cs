using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;

namespace SmartLMS.API.Controllers;

[Route("api/v1/me"), Authorize]
public sealed class MeController(IProfileService profile, IDeviceService devices, ICurrentUser user) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<UserDto>>> Get(CancellationToken ct) => Success(await profile.GetMeAsync(ct));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(UpdateProfileRequest request, CancellationToken ct) => Success(await profile.UpdateMeAsync(request, ct));

    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<object?>>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await profile.ChangePasswordAsync(request, ct);
        return Done("Password changed.");
    }

    [HttpGet("devices")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserDeviceDto>>>> Devices(CancellationToken ct) =>
        Success(await devices.ListForUserAsync(user.UserId!.Value, ct));

    [HttpDelete("devices/{deviceId:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> RemoveDevice(Guid deviceId, CancellationToken ct)
    {
        await devices.RemoveOwnDeviceAsync(deviceId, ct);
        return Done("Device removed.");
    }
}
