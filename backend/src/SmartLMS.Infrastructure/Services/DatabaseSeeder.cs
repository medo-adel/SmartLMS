using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

/// <summary>
/// Idempotent. Always seeds the six roles. A platform SuperAdmin and a demo tenant are created only when the matching configuration is supplied
/// (user-secrets or environment variables). No credential exists in source control; nothing is created with a default password.
///   Seed:SuperAdminEmail / Seed:SuperAdminPassword   -> platform SuperAdmin
///   Seed:DemoTenant=true                              -> tenant "demo"
///   Seed:DemoAdminEmail / Seed:DemoAdminPassword      -> TenantAdmin of the demo tenant
/// </summary>
public sealed class DatabaseSeeder(ApplicationDbContext db, RoleManager<ApplicationRole> roles, ITenantContext tenant, UserProvisioner provisioner,
    IConfiguration config, ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var name in RoleNames.All)
        {
            if (!await roles.RoleExistsAsync(name)) await roles.CreateAsync(new ApplicationRole(name));
        }

        var saEmail = config["Seed:SuperAdminEmail"];
        var saPassword = config["Seed:SuperAdminPassword"];
        if (!string.IsNullOrWhiteSpace(saEmail) && !string.IsNullOrWhiteSpace(saPassword))
        {
            tenant.Set(null, isPlatformScope: true);
            if (await provisioner.FindByEmailAsync(saEmail, ct) is null)
            {
                await provisioner.CreateAsync(saEmail, "Platform Admin", saPassword, [RoleNames.SuperAdmin], null, "en", true, ct);
                logger.LogInformation("Seeded platform SuperAdmin {Email}", saEmail);
            }
        }

        if (config.GetValue<bool>("Seed:DemoTenant"))
        {
            tenant.Set(null, isPlatformScope: true);
            var demo = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == "demo", ct);
            if (demo is null)
            {
                demo = new Tenant { Name = "Demo Academy", Slug = "demo", Subdomain = "demo", MaxMobileDevices = 1, MaxWebDevices = 1 };
                db.Tenants.Add(demo);
                await db.SaveChangesAsync(ct);
                using (tenant.Use(demo.Id))
                {
                    foreach (var key in TenantService.FeatureKeys) db.TenantFeatureFlags.Add(new TenantFeatureFlag { TenantId = demo.Id, FeatureKey = key });
                    await db.SaveChangesAsync(ct);
                }
                logger.LogInformation("Seeded demo tenant");
            }
            var email = config["Seed:DemoAdminEmail"];
            var pw = config["Seed:DemoAdminPassword"];
            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(pw))
            {
                using (tenant.Use(demo.Id))
                {
                    if (await provisioner.FindByEmailAsync(email, ct) is null)
                        await provisioner.CreateAsync(email, "Demo Admin", pw, [RoleNames.TenantAdmin], null, "en", true, ct);
                }
            }
        }
        tenant.Set(null, false);
    }
}
