using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services;

namespace SmartLMS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the DbContext (SQL Server), Identity, token/auth services and options. Everything reads configuration lazily so test hosts can override it.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiServiceOptions>(configuration.GetSection(AiServiceOptions.SectionName));
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => System.Text.Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "Jwt:SigningKey must be set to at least 32 bytes (use dotnet user-secrets or the Jwt__SigningKey environment variable).")
            .ValidateOnStart();
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TenantContext>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                // UserName is "<tenantSlug>:<email>"; email uniqueness is per tenant and enforced by the (TenantId, NormalizedUserName) index.
                o.User.AllowedUserNameCharacters = string.Empty;
                o.User.RequireUniqueEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<ITokenService, TokenService>();
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        // Real sending needs an explicit opt-in plus a server and from-address. Anything less keeps the do-nothing transport.
        services.AddScoped<IEmailTransport>(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value;
            return o.Enabled && !string.IsNullOrWhiteSpace(o.FromAddress) && (!string.IsNullOrWhiteSpace(o.Host) || !string.IsNullOrWhiteSpace(o.PickupDirectory))
                ? ActivatorUtilities.CreateInstance<SmtpEmailTransport>(sp)
                : ActivatorUtilities.CreateInstance<DisabledEmailTransport>(sp);
        });
        services.AddScoped<IOtpSender, EmailOtpSender>();
        services.AddScoped<ILogoService, LogoService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.LmsService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.AssessmentService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.GamificationService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.CertificateService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.ParentService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.JobService>();
        services.AddSingleton<SmartLMS.Application.Interfaces.IRealtime, SmartLMS.Infrastructure.Services.Lms.NullRealtime>();   // replaced by the SignalR implementation in the API host
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.CommunityService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.TransactionalMailer>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.MaintenanceJobs>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.ContentService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.Notifier>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.PeerReviewJob>();
        services.AddSingleton<SmartLMS.Application.Interfaces.IJobQueue, SmartLMS.Infrastructure.Services.Lms.NullJobQueue>();   // replaced by the Hangfire queue in the API host when Hangfire is on
        services.AddSingleton<SmartLMS.Application.Interfaces.IPushSender>(sp =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var path = cfg["Push:Fcm:ServiceAccountPath"];
            if (!cfg.GetValue<bool>("Push:Fcm:Enabled") || string.IsNullOrWhiteSpace(path)) return new SmartLMS.Infrastructure.Services.NullPushSender();
            return new SmartLMS.Infrastructure.Services.FcmPushSender(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, File.ReadAllText(path));
        });
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.LessonFileService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Lms.AssignmentService>();
        services.Configure<SmartLMS.Infrastructure.Services.Commerce.PaymentOptions>(configuration.GetSection("Payments"));
        services.Configure<SmartLMS.Infrastructure.Services.Commerce.WebhookOptions>(configuration.GetSection("Webhooks"));
        services.AddHttpClient("webhooks");
        services.AddHttpClient("paymob");
        services.AddHttpClient<SmartLMS.Application.Interfaces.IAiServiceClient, SmartLMS.Infrastructure.Services.Ai.AiServiceClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiServiceOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/"); c.Timeout = TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 5, 600));
        });
        services.AddScoped<SmartLMS.Infrastructure.Services.Ai.AiJobService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Ai.AiService>();
        services.AddSingleton<SmartLMS.Infrastructure.Services.Commerce.GatewayRegistry>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.WalletLedger>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.AuditWriter>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.CommerceService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.WalletService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.BatchGenerator>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.RevenueService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.AdminService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.WebhookService>();
        services.AddScoped<SmartLMS.Infrastructure.Services.Commerce.BackgroundJobRunner>();
        services.AddScoped<UserProvisioner>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<DatabaseSeeder>();
        return services;
    }
}
