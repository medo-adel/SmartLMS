using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

/// <summary>Lesson media and downloadable resources on local disk (Storage:Root). The file type comes from the bytes, never from the client's name or Content-Type.</summary>
public sealed class LessonFileService(ApplicationDbContext db, ICurrentUser me, LmsService lms, IOptions<StorageOptions> storage)
{
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private string Root => Path.GetFullPath(storage.Value.Root);
    private string Resolve(string rel) { var f = Path.GetFullPath(Path.Combine(Root, rel)); if (!f.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ForbiddenAppException("FORBIDDEN", "Invalid storage path."); return f; }

    private static (string Ext, string Type, bool Media)? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b.Slice(4, 4).SequenceEqual("ftyp"u8)) return ("mp4", "video/mp4", true);
        if (b.Length >= 4 && b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return ("webm", "video/webm", true);
        if (b.Length >= 3 && b[..3].SequenceEqual("ID3"u8)) return ("mp3", "audio/mpeg", true);
        if (b.Length >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return ("mp3", "audio/mpeg", true);
        if (b.Length >= 5 && b[..5].SequenceEqual("%PDF-"u8)) return ("pdf", "application/pdf", false);
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("png", "image/png", false);
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("jpg", "image/jpeg", false);
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 3 && b[3] == 4) return ("zip", "application/zip", false);
        return null;
    }

    private async Task<Lesson> OwnedLessonAsync(Guid id, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lesson not found.");
        var c = await db.Courses.FirstAsync(x => x.Id == l.CourseId, ct);
        if (!await new CourseAccess(db, me).IsAuthorAsync(c, ct)) throw new NotFoundException("Lesson not found.");
        return l;
    }

    private async Task<FileAsset> SaveAsync(Stream content, long max, Func<(string Ext, string Type, bool Media), bool> accept, string originalName, FileAssetPurpose purpose, string folder, CancellationToken ct)
    {
        var head = new byte[16]; var got = 0; int n;
        while (got < 16 && (n = await content.ReadAsync(head.AsMemory(got, 16 - got), ct)) > 0) got += n;
        var kind = Sniff(head.AsSpan(0, got));
        if (kind is null || !accept(kind.Value)) throw new ValidationAppException("This file type is not allowed.");
        var rel = Path.Combine("tenants", Tid.ToString("N"), folder, $"{Guid.NewGuid():N}.{kind.Value.Ext}"); var full = Resolve(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        long total = got; using var sha = SHA256.Create(); sha.TransformBlock(head, 0, got, null, 0);
        try
        {
            await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
            {
                await fs.WriteAsync(head.AsMemory(0, got), ct);
                var buf = new byte[81920];
                while ((n = await content.ReadAsync(buf, ct)) > 0)
                {
                    total += n; if (total > max) throw new ValidationAppException($"File must be {max / 1024 / 1024} MB or smaller.");
                    sha.TransformBlock(buf, 0, n, null, 0); await fs.WriteAsync(buf.AsMemory(0, n), ct);
                }
            }
        }
        catch { if (File.Exists(full)) File.Delete(full); throw; }
        sha.TransformFinalBlock([], 0, 0);
        var name = Path.GetFileName(originalName ?? "file"); if (name.Length > 200) name = name[..200];
        var fa = new FileAsset { TenantId = Tid, OriginalName = name, StoragePath = rel.Replace('\\', '/'), ContentType = kind.Value.Type, SizeBytes = total, Sha256 = Convert.ToHexString(sha.Hash!).ToLowerInvariant(), UploadedBy = Uid, Purpose = purpose };
        db.FileAssets.Add(fa); return fa;
    }

    public async Task<object> UploadMediaAsync(Guid lessonId, Stream content, string name, CancellationToken ct)
    {
        var l = await OwnedLessonAsync(lessonId, ct);
        if (l.LessonType is not (LessonType.Video or LessonType.Audio or LessonType.Pdf)) throw new ValidationAppException("Only Video, Audio and Pdf lessons take a media file.");
        var fa = await SaveAsync(content, storage.Value.MaxMediaBytes, k => l.LessonType switch { LessonType.Video => k.Type.StartsWith("video/"), LessonType.Audio => k.Type.StartsWith("audio/"), _ => k.Type == "application/pdf" }, name, FileAssetPurpose.CourseMedia, "media", ct);
        if (l.LessonType is LessonType.Video or LessonType.Audio) fa.MeasuredDurationSeconds = MediaDuration.TryMeasure(Resolve(fa.StoragePath));   // from the stored bytes, null when the format is not measurable
        l.ContentFileId = fa.Id; await db.SaveChangesAsync(ct);
        return new { fileId = fa.Id, fa.ContentType, fa.SizeBytes, measuredDurationSeconds = fa.MeasuredDurationSeconds };
    }

    public async Task<(Stream Content, string ContentType)> StreamAsync(Guid lessonId, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct) ?? throw new NotFoundException("Lesson not found.");
        await lms.GetLessonAsync(lessonId, ct);   // enrollment, publish state, drip and prerequisites are enforced there
        var f = l.ContentFileId is { } fid ? await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fid, ct) : null;
        if (f is null) throw new NotFoundException("This lesson has no media file.");
        var p = Resolve(f.StoragePath); if (!File.Exists(p)) throw new NotFoundException("This lesson has no media file.");
        return (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read), f.ContentType);
    }

    /// <summary>Lists a lesson's resources. Students see only downloadable ones, and only when they can open the lesson.</summary>
    public async Task<object> ListResourcesAsync(Guid lessonId, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct) ?? throw new NotFoundException("Lesson not found.");
        var c = await db.Courses.FirstAsync(x => x.Id == l.CourseId, ct); var author = await new CourseAccess(db, me).IsGraderAsync(c, ct);
        if (!author) await lms.GetLessonAsync(lessonId, ct);
        return await (from r in db.LessonResources.AsNoTracking() join f in db.FileAssets.AsNoTracking() on r.FileId equals f.Id
                      where r.LessonId == lessonId && (author || r.IsDownloadable) orderby r.CreatedAtUtc
                      select new { r.Id, r.Title, r.IsDownloadable, f.ContentType, f.SizeBytes, fileName = f.OriginalName }).ToListAsync(ct);
    }

    public async Task<object> UploadResourceAsync(Guid lessonId, Stream content, string name, string? title, bool downloadable, CancellationToken ct)
    {
        var l = await OwnedLessonAsync(lessonId, ct);
        if (await db.LessonResources.CountAsync(r => r.LessonId == lessonId, ct) >= 30) throw new ValidationAppException("At most 30 resources per lesson.");
        var fa = await SaveAsync(content, storage.Value.MaxResourceBytes, _ => true, name, FileAssetPurpose.LessonResource, "resources", ct);
        await db.SaveChangesAsync(ct);
        var t = string.IsNullOrWhiteSpace(title) ? fa.OriginalName : title.Trim(); if (t.Length > 200) throw new ValidationAppException("Title is too long.");
        var r = new LessonResource { TenantId = Tid, LessonId = l.Id, Title = t, FileId = fa.Id, IsDownloadable = downloadable, CreatedAtUtc = DateTime.UtcNow };
        db.LessonResources.Add(r); await db.SaveChangesAsync(ct);
        return new { id = r.Id, r.Title, r.IsDownloadable, fa.ContentType, fa.SizeBytes };
    }

    public async Task<(Stream Content, string ContentType, string Name)> DownloadResourceAsync(Guid lessonId, Guid resourceId, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct) ?? throw new NotFoundException("Lesson not found.");
        var c = await db.Courses.FirstAsync(x => x.Id == l.CourseId, ct); var author = await new CourseAccess(db, me).IsGraderAsync(c, ct);
        if (!author) await lms.GetLessonAsync(lessonId, ct);
        var r = await db.LessonResources.FirstOrDefaultAsync(x => x.Id == resourceId && x.LessonId == lessonId, ct) ?? throw new NotFoundException("Resource not found.");
        if (!author && !r.IsDownloadable) throw new ForbiddenAppException("NOT_DOWNLOADABLE", "This resource cannot be downloaded.");
        var f = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.FileId, ct) ?? throw new NotFoundException("Resource not found.");
        var p = Resolve(f.StoragePath); if (!File.Exists(p)) throw new NotFoundException("Resource not found.");
        return (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read), f.ContentType, f.OriginalName);
    }
}
