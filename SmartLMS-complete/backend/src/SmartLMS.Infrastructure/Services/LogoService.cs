using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Dtos;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services;

public sealed class LogoService(ApplicationDbContext db, ICurrentTenant tenant, ICurrentUser user, IOptions<StorageOptions> storage) : ILogoService
{
    public const string PublicUrl = "/api/v1/tenants/current/logo";

    private static (string Ext, string Type)? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("png", "image/png");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("jpg", "image/jpeg");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return ("webp", "image/webp");
        return null;
    }

    private string Root => Path.GetFullPath(storage.Value.Root);

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ForbiddenAppException("FORBIDDEN", "Invalid storage path.");
        return full;
    }

    public async Task<TenantDto> UploadAsync(Stream content, long length, CancellationToken ct)
    {
        var tid = tenant.TenantId ?? throw new ValidationAppException("A tenant is required.");
        var uid = user.UserId ?? throw new UnauthorizedAppException();
        var max = storage.Value.MaxLogoBytes;
        if (length > max) throw new ValidationAppException($"Logo must be {max / 1024 / 1024} MB or smaller.");

        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await content.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > max) throw new ValidationAppException($"Logo must be {max / 1024 / 1024} MB or smaller.");
        }
        var bytes = ms.ToArray();
        // The type comes from the bytes, never from the client's file name or Content-Type. SVG is refused on purpose (it can carry scripts).
        var kind = Sniff(bytes) ?? throw new ValidationAppException("Logo must be a PNG, JPEG or WebP image.");

        var relative = Path.Combine("tenants", tid.ToString("N"), "branding", $"logo-{Guid.NewGuid():N}.{kind.Ext}");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes, ct);

        var t = await db.Tenants.FirstAsync(x => x.Id == tid, ct);
        var old = await db.FileAssets.Where(f => f.Purpose == FileAssetPurpose.Other && f.OriginalName == "tenant-logo").ToListAsync(ct);
        db.FileAssets.RemoveRange(old); // soft delete
        db.FileAssets.Add(new FileAsset
        {
            TenantId = tid, OriginalName = "tenant-logo", StoragePath = relative.Replace('\\', '/'), ContentType = kind.Type, SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), UploadedBy = uid, Purpose = FileAssetPurpose.Other,
        });
        t.LogoUrl = PublicUrl;
        await db.SaveChangesAsync(ct);
        return new TenantDto(t.Id, t.Name, t.Slug, t.Subdomain, t.LogoUrl, t.PrimaryColor, t.SecondaryColor, t.AccentColor, t.DefaultLanguage, t.TimeZoneId, t.Currency, t.MaxMobileDevices, t.MaxWebDevices, t.IsActive);
    }

    public async Task<(Stream Content, string ContentType)> OpenAsync(CancellationToken ct)
    {
        var tid = tenant.TenantId ?? throw new ValidationAppException("A tenant is required.");
        // FileAssets is tenant-filtered, so only the current tenant's logo can be found.
        var f = await db.FileAssets.AsNoTracking().Where(x => x.Purpose == FileAssetPurpose.Other && x.OriginalName == "tenant-logo")
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("No logo.");
        if (!f.StoragePath.StartsWith($"tenants/{tid:N}/", StringComparison.Ordinal)) throw new NotFoundException("No logo.");
        var path = Resolve(f.StoragePath);
        if (!File.Exists(path)) throw new NotFoundException("No logo.");
        return (File.OpenRead(path), f.ContentType);
    }
}
