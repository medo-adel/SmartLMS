using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;

namespace SmartLMS.API.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse<T>> Success<T>(T data, string? message = null) => Ok(ApiResponse<T>.Ok(data, message));

    protected ActionResult<ApiResponse<object?>> Done(string message) => Ok(ApiResponse<object?>.Ok(null, message));

    protected ObjectResult NotConfigured(string what) =>
        StatusCode(StatusCodes.Status501NotImplemented, ApiResponse<object>.Fail($"{what} is not configured in this build.", code: "NOT_IMPLEMENTED"));
}
