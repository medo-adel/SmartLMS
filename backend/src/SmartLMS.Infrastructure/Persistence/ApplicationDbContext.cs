using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Common;
using SmartLMS.Domain.Entities;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Persistence.Generated;

namespace SmartLMS.Infrastructure.Persistence;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenant currentTenant)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid, IdentityUserClaim<Guid>, IdentityUserRole<Guid>, IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>(options)
{
    /// <summary>Read by every query filter. EF re-evaluates it per context instance, so a pooled/cached model never leaks another tenant.</summary>
    public Guid? CurrentTenantId => currentTenant.TenantId;
    public bool IsPlatformScope => currentTenant.IsPlatformScope;
    public ICurrentTenant Tenant => currentTenant;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var sqlServer = Database.IsSqlServer();

        builder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.FullName).HasMaxLength(200);
            b.Property(u => u.AvatarUrl).HasMaxLength(500);
            b.Property(u => u.PreferredLanguage).HasMaxLength(10);
            b.Property(u => u.UserName).HasMaxLength(256).IsRequired();
            b.Property(u => u.NormalizedUserName).HasMaxLength(256).IsRequired();
            b.Property(u => u.Email).HasMaxLength(256);
            b.Property(u => u.NormalizedEmail).HasMaxLength(256);
            b.Property(u => u.PhoneNumber).HasMaxLength(32);
            // Identity's default unique index on NormalizedUserName alone would stop two tenants from sharing an email. Replace it.
            b.HasIndex(u => u.NormalizedUserName, "UserNameIndex").IsUnique(false);
            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }).IsUnique().HasDatabaseName("UX_AspNetUsers_TenantId_NormalizedUserName")
                .HasFilter("[IsDeleted] = 0 AND [TenantId] IS NOT NULL");
            b.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("UX_AspNetUsers_PlatformUserName")
                .HasFilter("[IsDeleted] = 0 AND [TenantId] IS NULL");
            b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<ApplicationRole>(b => { b.Property(r => r.Name).HasMaxLength(256).IsRequired(); b.Property(r => r.NormalizedName).HasMaxLength(256).IsRequired(); });

        GeneratedModel.Apply(builder, sqlServer);
        // Identity defaults to ON DELETE CASCADE on its child tables. Users and roles are soft-deleted, never hard-deleted, and schema.sql uses NO ACTION
        // everywhere, so make the EF model match instead of relying on a cascade graph nobody exercises.
        foreach (var fk in builder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.NoAction;
        ApplyTenantFilters(builder);
    }

    private static readonly MethodInfo TenantSoft = typeof(ApplicationDbContext).GetMethod(nameof(SetTenantSoft), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo TenantOnly = typeof(ApplicationDbContext).GetMethod(nameof(SetTenantOnly), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo NullableSoft = typeof(ApplicationDbContext).GetMethod(nameof(SetNullableSoft), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo NullableOnly = typeof(ApplicationDbContext).GetMethod(nameof(SetNullableOnly), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void ApplyTenantFilters(ModelBuilder builder)
    {
        foreach (var et in builder.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList())
        {
            var clr = et.ClrType;
            if (clr == typeof(Tenant))
            {
                builder.Entity<Tenant>().HasQueryFilter(t => !t.IsDeleted && (IsPlatformScope || (CurrentTenantId != null && t.Id == CurrentTenantId)));
                continue;
            }
            var tenant = typeof(ITenantEntity).IsAssignableFrom(clr);
            var nullable = typeof(INullableTenantEntity).IsAssignableFrom(clr);
            var soft = typeof(ISoftDeletable).IsAssignableFrom(clr);
            if (!tenant && !nullable)
            {
                continue;
            }
            var method = (tenant, soft) switch
            {
                (true, true) => TenantSoft,
                (true, false) => TenantOnly,
                (false, true) => NullableSoft,
                _ => NullableOnly,
            };
            method.MakeGenericMethod(clr).Invoke(this, [builder]);
        }
    }

    // Tenant-owned rows: visible only when TenantId equals the request's tenant. No tenant resolved => no rows (fails closed). Platform scope does NOT widen this.
    private void SetTenantSoft<T>(ModelBuilder b) where T : class, ITenantEntity, ISoftDeletable =>
        b.Entity<T>().HasQueryFilter(e => !e.IsDeleted && CurrentTenantId != null && e.TenantId == CurrentTenantId);

    private void SetTenantOnly<T>(ModelBuilder b) where T : class, ITenantEntity =>
        b.Entity<T>().HasQueryFilter(e => CurrentTenantId != null && e.TenantId == CurrentTenantId);

    // Platform-aware rows: tenant rows for the current tenant, plus platform rows (TenantId NULL) only in platform scope.
    private void SetNullableSoft<T>(ModelBuilder b) where T : class, INullableTenantEntity, ISoftDeletable =>
        b.Entity<T>().HasQueryFilter(e => !e.IsDeleted && ((CurrentTenantId != null && e.TenantId == CurrentTenantId) || (IsPlatformScope && e.TenantId == null)));

    private void SetNullableOnly<T>(ModelBuilder b) where T : class, INullableTenantEntity =>
        b.Entity<T>().HasQueryFilter(e => (CurrentTenantId != null && e.TenantId == CurrentTenantId) || (IsPlatformScope && e.TenantId == null));
}
