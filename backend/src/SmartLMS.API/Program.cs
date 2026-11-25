using Hangfire;
using Microsoft.AspNetCore.SignalR;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SmartLMS.API.Filters;
using SmartLMS.API.Infrastructure;
using SmartLMS.API.Middleware;
using SmartLMS.API.Swagger;
using SmartLMS.Application.Common;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase)
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        var errors = ctx.ModelState.Where(kv => kv.Value?.Errors.Count > 0).SelectMany(kv => kv.Value!.Errors.Select(e => $"{kv.Key}: {(string.IsNullOrEmpty(e.ErrorMessage) ? "invalid value" : e.ErrorMessage)}"));
        return new ObjectResult(ApiResponse<object>.Fail("Validation failed.", errors, "VALIDATION_FAILED")) { StatusCode = StatusCodes.Status400BadRequest };
    });
builder.Services.AddValidatorsFromAssemblyContaining<SmartLMS.Application.DeviceInfoValidator>();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>((o, jwt) =>
{
    var j = jwt.Value;
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = j.Issuer,
        ValidateAudience = true, ValidAudience = j.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j.SigningKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub", RoleClaimType = "role",
    };
    // 401/403 produced by the framework must use the same envelope as every other error.
    o.Events = new JwtBearerEvents
    {
        // WebSockets cannot send an Authorization header, so a hub connection may carry the token in the query string. Only hub paths accept it.
        OnMessageReceived = ctx => { if (ctx.HttpContext.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query.TryGetValue("access_token", out var t)) ctx.Token = t; return Task.CompletedTask; },
        OnChallenge = ctx => { ctx.HandleResponse(); return ErrorWriter.WriteAsync(ctx.HttpContext, 401, "UNAUTHORIZED", "Authentication is required."); },
        OnForbidden = ctx => ErrorWriter.WriteAsync(ctx.HttpContext, 403, "FORBIDDEN", "You do not have permission for this action."),
    };
});
builder.Services.AddAuthorization();
var hangfireOn = builder.Configuration.GetValue<bool>("Hangfire:Enabled");
if (hangfireOn)
{
    var storage = builder.Configuration["Hangfire:Storage"] ?? "SqlServer";
    if (storage.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
    {
        if (builder.Environment.IsProduction()) throw new InvalidOperationException("Hangfire:Storage=InMemory loses every job on restart and is not allowed in Production.");
        builder.Services.AddHangfire(c => c.UseInMemoryStorage());
    }
    else builder.Services.AddHangfire(c => c.UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"), new Hangfire.SqlServer.SqlServerStorageOptions { PrepareSchemaIfNecessary = true }));
    builder.Services.AddSingleton<SmartLMS.Application.Interfaces.IJobQueue, SmartLMS.API.Infrastructure.HangfireJobQueue>();
    builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Clamp(builder.Configuration.GetValue("Hangfire:Workers", 2), 1, 8));
}
builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 16 * 1024; o.AddFilter<SmartLMS.API.Hubs.HubSecurityFilter>(); });
builder.Services.AddScoped<SmartLMS.API.Hubs.HubSecurityFilter>();
builder.Services.AddScoped<SmartLMS.API.Infrastructure.HubIdentity>();
builder.Services.AddSingleton<SmartLMS.Application.Interfaces.IRealtime, SmartLMS.API.Hubs.SignalRRealtime>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx =>
    {
        var limit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:AuthPermitsPerMinute", 10);
        return RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    o.AddPolicy("voucher", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:VoucherPermitsPerMinute", 20), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = (ctx, ct) => new ValueTask(ErrorWriter.WriteAsync(ctx.HttpContext, 429, "RATE_LIMITED", "Too many requests. Try again in a minute."));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SmartLMS API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Access token from /auth/login." });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    c.OperationFilter<TenantDeviceHeaderFilter>();
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<DeviceBindingMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SmartLMS.API.Hubs.NotificationsHub>("/hubs/notifications").RequireAuthorization();
app.MapHub<SmartLMS.API.Hubs.ChatHub>("/hubs/chat").RequireAuthorization();
app.MapHub<SmartLMS.API.Hubs.LiveHub>("/hubs/live").RequireAuthorization();

if (app.Configuration.GetValue<bool>("Database:EnsureCreated"))
{
    // Test hosts only (SQLite). Real deployments use migrations.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
}
if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
}
if (app.Configuration.GetValue<bool>("Seed:Enabled"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

if (hangfireOn)
{
    // The dashboard is deliberately not mapped: it would show every tenant's jobs. Jobs are registered after the database exists.
    app.Services.GetRequiredService<Hangfire.IRecurringJobManager>().AddOrUpdate<SmartLMS.Infrastructure.Services.Lms.MaintenanceJobs>(SmartLMS.Infrastructure.Services.Lms.MaintenanceJobs.ExpireEnrollmentsId, j => j.ExpireEnrollmentsAsync(), "*/15 * * * *");
    app.Services.GetRequiredService<Hangfire.IRecurringJobManager>().AddOrUpdate<SmartLMS.Infrastructure.Services.Commerce.BackgroundJobRunner>("webhook-retries", j => j.RetryWebhooksAsync(), "*/5 * * * *");
}

app.Run();

/// <summary>Makes the entry point visible to WebApplicationFactory in the test project.</summary>
public partial class Program;
