using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Infrastructure.Services.Commerce;

namespace SmartLMS.API.Controllers;

/// <summary>Read-only endpoints for API keys (extra, not in the contract). Authenticated by X-Api-Key plus X-Tenant-Id, no user session.</summary>
[Route("api/v1/integration"), AllowAnonymous]
public sealed class IntegrationController(WebhookService wh) : ApiControllerBase
{
    [HttpGet("orders")] public async Task<ActionResult<ApiResponse<object>>> Orders([FromQuery] PagedQueryLite q, CancellationToken ct)
    { await wh.AuthenticateKeyAsync(Request.Headers["X-Api-Key"].FirstOrDefault(), "orders:read", ct); return Success(await wh.IntegrationOrdersAsync(q, ct)); }
    [HttpGet("enrollments")] public async Task<ActionResult<ApiResponse<object>>> Enrollments([FromQuery] PagedQueryLite q, CancellationToken ct)
    { await wh.AuthenticateKeyAsync(Request.Headers["X-Api-Key"].FirstOrDefault(), "enrollments:read", ct); return Success(await wh.IntegrationEnrollmentsAsync(q, ct)); }
}
