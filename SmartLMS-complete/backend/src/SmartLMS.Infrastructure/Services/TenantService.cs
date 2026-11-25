using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Common;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

public sealed class TenantService(ApplicationDbContext db, ITenantContext tenant, UserProvisioner provisioner, ICurrentUser currentUser, TimeProvider clock) : ITenantService
{
    public static readonly string[] FeatureKeys = ["wallet", "affiliates", "live_classes", "ai_tutor", "parent_registration"];

    private static TenantDto ToDto(Tenant t) => new(t.Id, t.Name, t.Slug, t.Subdomain, t.LogoUrl, t.PrimaryColor, t.SecondaryColor, t.AccentColor,
        t.DefaultLanguage, t.TimeZoneId, t.Currency, t.MaxMobileDevices, t.MaxWebDevices, t.IsActive);

    private Guid RequireTenant() => tenant.TenantId ?? throw new ValidationAppException("A tenant is required (X-Tenant-Id).");

    public async Task<BrandingDto> GetBrandingAsync(CancellationToken ct)
    {
        var id = RequireTenant();
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Tenant not found.");
        var flags = await ListFlagsAsync(ct);
        return new BrandingDto(t.Name, t.Slug, t.LogoUrl, t.PrimaryColor, t.SecondaryColor, t.AccentColor, t.DefaultLanguage, flags);
    }

    public async Task<PagedResult<TenantDto>> ListAsync(PagedQuery paging, CancellationToken ct)
    {
        var q = db.Tenants.AsNoTracking();
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(t => t.Name).ThenBy(t => t.Id).Skip(paging.Skip).Take(paging.NormalizedPageSize).ToListAsync(ct);
        return new PagedResult<TenantDto>(items.Select(ToDto).ToList(), paging.NormalizedPage, paging.NormalizedPageSize, total);
    }

    public async Task<TenantDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Tenant not found."));

    public async Task<TenantDto> CreateAsync(CreateTenantRequest r, CancellationToken ct)
    {
        var slug = r.Slug.Trim().ToLowerInvariant();
        if (slug == UserProvisioner.PlatformPrefix) throw new ValidationAppException("This slug is reserved.");
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug || (r.Subdomain != null && t.Subdomain == r.Subdomain), ct))
            throw new ConflictAppException("TENANT_EXISTS", "A tenant with this slug or subdomain already exists.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = new Tenant
        {
            Name = r.Name.Trim(), Slug = slug, Subdomain = r.Subdomain, DefaultLanguage = r.DefaultLanguage, TimeZoneId = r.TimeZoneId, Currency = r.Currency.ToUpperInvariant(),
            MaxMobileDevices = r.MaxMobileDevices, MaxWebDevices = r.MaxWebDevices,
        };
        db.Tenants.Add(t);
        await db.SaveChangesAsync(ct);

        using (tenant.Use(t.Id))
        {
            foreach (var key in FeatureKeys) db.TenantFeatureFlags.Add(new TenantFeatureFlag { TenantId = t.Id, FeatureKey = key, IsEnabled = false });
            await db.SaveChangesAsync(ct);
            await provisioner.CreateAsync(r.AdminEmail, r.AdminFullName, r.AdminPassword, [RoleNames.TenantAdmin], null, r.DefaultLanguage, emailConfirmed: true, ct);
        }
        await tx.CommitAsync(ct);
        return ToDto(t);
    }

    public async Task<TenantDto> UpdateAsync(Guid id, UpdateTenantRequest r, CancellationToken ct)
    {
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Tenant not found.");
        if (r.Subdomain is not null && await db.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id != id && x.Subdomain == r.Subdomain, ct))
            throw new ConflictAppException("TENANT_EXISTS", "Subdomain already in use.");
        t.Name = r.Name.Trim(); t.Subdomain = r.Subdomain; t.IsActive = r.IsActive; t.MaxMobileDevices = r.MaxMobileDevices; t.MaxWebDevices = r.MaxWebDevices;
        t.DefaultLanguage = r.DefaultLanguage; t.TimeZoneId = r.TimeZoneId; t.Currency = r.Currency.ToUpperInvariant();
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task<TenantDto> UpdateSettingsAsync(TenantSettingsRequest r, CancellationToken ct)
    {
        var id = RequireTenant();
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Tenant not found.");
        t.Name = r.Name.Trim(); t.PrimaryColor = r.PrimaryColor; t.SecondaryColor = r.SecondaryColor; t.AccentColor = r.AccentColor;
        t.DefaultLanguage = r.DefaultLanguage; t.TimeZoneId = r.TimeZoneId; t.Currency = r.Currency.ToUpperInvariant();
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task<IReadOnlyList<FeatureFlagDto>> ListFlagsAsync(CancellationToken ct)
    {
        var rows = await db.TenantFeatureFlags.AsNoTracking().OrderBy(f => f.FeatureKey).Select(f => new FeatureFlagDto(f.FeatureKey, f.IsEnabled)).ToListAsync(ct);
        return rows;
    }

    public async Task<FeatureFlagDto> SetFlagAsync(string key, bool enabled, CancellationToken ct)
    {
        var id = RequireTenant();
        if (!FeatureKeys.Contains(key)) throw new NotFoundException("Unknown feature key.");
        var f = await db.TenantFeatureFlags.FirstOrDefaultAsync(x => x.FeatureKey == key, ct);
        if (f is null) { f = new TenantFeatureFlag { TenantId = id, FeatureKey = key }; db.TenantFeatureFlags.Add(f); }
        f.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);
        return new FeatureFlagDto(f.FeatureKey, f.IsEnabled);
    }

    public async Task<SupportAccessDto> GrantSupportAccessAsync(Guid tenantId, int? minutes, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAppException();
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) throw new NotFoundException("Tenant not found.");
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(Math.Clamp(minutes ?? 60, 1, 480));
        using (tenant.Use(tenantId))
        {
            var m = await db.UserTenantMemberships.FirstOrDefaultAsync(x => x.UserId == actor, ct);
            if (m is null) { m = new UserTenantMembership { TenantId = tenantId, UserId = actor }; db.UserTenantMemberships.Add(m); }
            m.Kind = UserTenantMembershipKind.SupportAccess; m.GrantedBy = actor; m.ExpiresAtUtc = expires; m.RevokedAtUtc = null;
            await db.SaveChangesAsync(ct);
        }
        return new SupportAccessDto(tenantId, expires);
    }

    public async Task RevokeSupportAccessAsync(Guid tenantId, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAppException();
        using (tenant.Use(tenantId))
        {
            var m = await db.UserTenantMemberships.FirstOrDefaultAsync(x => x.UserId == actor && x.Kind == UserTenantMembershipKind.SupportAccess, ct);
            if (m is null) return;
            m.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }
}
