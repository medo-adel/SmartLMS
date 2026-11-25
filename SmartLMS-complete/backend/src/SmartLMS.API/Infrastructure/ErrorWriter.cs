using System.Text.Json;
using SmartLMS.Application.Common;

namespace SmartLMS.API.Infrastructure;

public static class ErrorWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext ctx, int status, string code, string message, IEnumerable<string>? errors = null)
    {
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(message, errors, code), Options));
    }
}
