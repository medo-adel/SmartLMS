using SmartLMS.API.Infrastructure;
using SmartLMS.Domain.Exceptions;

namespace SmartLMS.API.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to write.
        }
        catch (Exception ex)
        {
            var (status, code, message, errors) = ex switch
            {
                ValidationAppException v => (400, v.Code, v.Message, v.Errors),
                AppException a => (a.StatusCode, a.Code, a.Message, (IReadOnlyList<string>)[]),
                NotFoundException => (404, "NOT_FOUND", ex.Message, []),
                DomainException => (400, "DOMAIN_RULE_VIOLATED", ex.Message, []),
                _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.", (IReadOnlyList<string>)[]),
            };

            if (status == 500)
            {
                logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            }

            if (context.Response.HasStarted)
            {
                throw;
            }

            await ErrorWriter.WriteAsync(context, status, code, message, errors);
        }
    }
}
