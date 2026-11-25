using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.API;

/// <summary>Used only by "dotnet ef". Set SMARTLMS_DESIGN_CONNECTION to target another server; the default is the local SQL Server from the project brief.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public bool IsPlatformScope => false;
        public bool ActingViaSupportAccess => false;
    }

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("SMARTLMS_DESIGN_CONNECTION")
                 ?? "Server=.;Database=SmartLMSDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(cs, o => o.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;
        return new ApplicationDbContext(options, new NoTenant());
    }
}
