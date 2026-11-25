using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Enums;

namespace SmartLMS.API.Controllers;

[Route("api/v1/tenants")]
public sealed class TenantsController(ITenantService tenants, ILogoService logos) : ApiControllerBase
{
    [HttpGet("current/branding"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BrandingDto>>> Branding(CancellationToken ct) => Success(await tenants.GetBrandingAsync(ct));

    /// <summary>The current tenant's logo image (public, like branding). 404 when none was uploaded.</summary>
    [HttpGet("current/logo"), AllowAnonymous]
    public async Task<IActionResult> Logo(CancellationToken ct)
    {
        var (stream, type) = await logos.OpenAsync(ct);
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, type);
    }

    [HttpGet, Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<PagedResult<TenantDto>>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Success(await tenants.ListAsync(new PagedQuery(page, pageSize), ct));

    [HttpPost, Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<TenantDto>>> Create(CreateTenantRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<TenantDto>.Ok(await tenants.CreateAsync(request, ct)));

    [HttpGet("{id:guid}"), Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<TenantDto>>> Get(Guid id, CancellationToken ct) => Success(await tenants.GetAsync(id, ct));

    [HttpPut("{id:guid}"), Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<TenantDto>>> Update(Guid id, UpdateTenantRequest request, CancellationToken ct) => Success(await tenants.UpdateAsync(id, request, ct));

    /// <summary>Grants the calling SuperAdmin time-boxed access inside the tenant (default 60 minutes, maximum 480). Audited.</summary>
    [HttpPost("{id:guid}/support-access"), Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<SupportAccessDto>>> GrantSupportAccess(Guid id, SupportAccessRequest request, CancellationToken ct) =>
        Success(await tenants.GrantSupportAccessAsync(id, request.Minutes, ct));

    [HttpDelete("{id:guid}/support-access"), Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<object?>>> RevokeSupportAccess(Guid id, CancellationToken ct)
    {
        await tenants.RevokeSupportAccessAsync(id, ct);
        return Done("Support access revoked.");
    }
}

[Route("api/v1/tenant"), Authorize(Roles = RoleNames.TenantAdmin)]
public sealed class TenantSettingsController(ITenantService tenants, ILogoService logos) : ApiControllerBase
{
    [HttpPut("settings")]
    public async Task<ActionResult<ApiResponse<TenantDto>>> Settings(TenantSettingsRequest request, CancellationToken ct) => Success(await tenants.UpdateSettingsAsync(request, ct));

    /// <summary>Multipart upload, field name "file". PNG, JPEG or WebP, up to 2 MB. TenantAdmin only, stored under the tenant's own folder.</summary>
    [HttpPost("logo"), Consumes("multipart/form-data"), RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<TenantDto>>> Logo(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new SmartLMS.Domain.Exceptions.ValidationAppException("A file is required (form field \"file\").");
        await using var s = file.OpenReadStream();
        return Success(await logos.UploadAsync(s, file.Length, ct));
    }

    [HttpGet("feature-flags")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FeatureFlagDto>>>> Flags(CancellationToken ct) => Success(await tenants.ListFlagsAsync(ct));

    [HttpPut("feature-flags/{key}")]
    public async Task<ActionResult<ApiResponse<FeatureFlagDto>>> SetFlag(string key, SetFeatureFlagRequest request, CancellationToken ct) =>
        Success(await tenants.SetFlagAsync(key, request.IsEnabled, ct));
}
