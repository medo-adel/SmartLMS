using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Enums;

namespace SmartLMS.API.Controllers;

[Route("api/v1/users"), Authorize]
public sealed class UsersController(IUserService users, IDeviceService devices) : ApiControllerBase
{
    private const string Viewers = $"{RoleNames.SuperAdmin},{RoleNames.TenantAdmin},{RoleNames.Instructor}";
    private const string Managers = $"{RoleNames.TenantAdmin}";
    private const string DeviceManagers = $"{RoleNames.TenantAdmin},{RoleNames.Instructor}";

    [HttpGet, Authorize(Roles = Viewers)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> Search([FromQuery] string? search, [FromQuery] string? role, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Success(await users.SearchAsync(search, role, new PagedQuery(page, pageSize), ct));

    [HttpPost, Authorize(Roles = Managers)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create(CreateUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<UserDto>.Ok(await users.CreateAsync(request, ct)));

    [HttpGet("{id:guid}"), Authorize(Roles = Viewers)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Get(Guid id, CancellationToken ct) => Success(await users.GetAsync(id, ct));

    [HttpPut("{id:guid}"), Authorize(Roles = Managers)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => Success(await users.UpdateAsync(id, request, ct));

    [HttpPut("{id:guid}/roles"), Authorize(Roles = Managers)]
    public async Task<ActionResult<ApiResponse<UserDto>>> SetRoles(Guid id, SetRolesRequest request, CancellationToken ct) => Success(await users.SetRolesAsync(id, request, ct));

    [HttpPost("{id:guid}/deactivate"), Authorize(Roles = Managers)]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(Guid id, CancellationToken ct)
    {
        await users.DeactivateAsync(id, ct);
        return Done("User deactivated.");
    }

    [HttpGet("{id:guid}/devices"), Authorize(Roles = DeviceManagers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserDeviceDto>>>> Devices(Guid id, CancellationToken ct) => Success(await devices.ListForUserAsync(id, ct));

    [HttpPost("{id:guid}/devices/reset"), Authorize(Roles = DeviceManagers)]
    public async Task<ActionResult<ApiResponse<int>>> ResetDevices(Guid id, ResetDevicesRequest request, CancellationToken ct) =>
        Success(await devices.ResetUserDevicesAsync(id, request.Reason, ct), "Devices reset. The student can sign in on new devices.");
}
