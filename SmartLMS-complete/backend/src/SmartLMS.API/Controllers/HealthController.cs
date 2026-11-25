using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;

namespace SmartLMS.API.Controllers;

[ApiController]
[Route("api/v1/health")]
public sealed class HealthController : ControllerBase
{
    public sealed record HealthStatus(string Status, string Service, DateTime UtcTime);

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<HealthStatus>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<HealthStatus>> Get() =>
        Ok(ApiResponse<HealthStatus>.Ok(new HealthStatus("Healthy", "SmartLMS.API", DateTime.UtcNow)));
}
