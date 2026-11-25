namespace SmartLMS.API.Middleware;

/// <summary>
/// Adds conservative response headers to every API response. The API only returns JSON, files and API documentation,
/// so the content security policy forbids everything except what Swagger UI needs in Development.
/// Controllers that stream protected media set their own, stricter Cache-Control after this runs.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(self), geolocation=()";
            if (!env.IsDevelopment()) h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            if (!h.ContainsKey("Cache-Control") && ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        return next(ctx);
    }
}
