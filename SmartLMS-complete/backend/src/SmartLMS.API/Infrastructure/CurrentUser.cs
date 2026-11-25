using System.Security.Claims;
using SmartLMS.Application.Interfaces;

namespace SmartLMS.API.Infrastructure;

/// <summary>Set by the hub filter for the duration of one hub invocation. A socket's later messages do not carry their own HTTP request, so the connection's principal is used.</summary>
public sealed class HubIdentity { public ClaimsPrincipal? Principal { get; set; } }

public sealed class CurrentUser(IHttpContextAccessor accessor, HubIdentity hub) : ICurrentUser
{
    private HttpContext? Ctx => accessor.HttpContext;
    private ClaimsPrincipal? P => hub.Principal ?? Ctx?.User;

    private static Guid? G(string? v) => Guid.TryParse(v, out var g) ? g : null;

    public bool IsAuthenticated => P?.Identity?.IsAuthenticated == true;
    public Guid? UserId => G(P?.FindFirstValue("sub") ?? P?.FindFirstValue(ClaimTypes.NameIdentifier));
    public Guid? TokenTenantId => G(P?.FindFirstValue("tid"));
    public Guid? DeviceId => G(P?.FindFirstValue("did"));
    public bool IsInRole(string role) => P?.IsInRole(role) == true;
    public string? IpAddress => Ctx?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => Ctx?.TraceIdentifier;
}
