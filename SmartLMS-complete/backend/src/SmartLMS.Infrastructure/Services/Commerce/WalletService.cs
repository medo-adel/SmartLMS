using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.Infrastructure.Services.Commerce;

public sealed record VoucherBatchRequest(string Name, string? Prefix, int Quantity, string VoucherType, Guid? CourseId, decimal? WalletAmount, DateTime? ExpiresAtUtc);
public sealed record VoucherCodeRequest(string Code);
public sealed record RejectPaymentRequest(string Reason);
public sealed record ManualPaymentRequest(string Purpose, string Method, decimal Amount, Guid? OrderId, string? SenderReference, string? SenderPhoneOrAccount, DateTime? TransferredAtUtc);

/// <summary>Voucher code rules shared by checkout, redemption and generation. Codes are stored only as a SHA-256 hash plus the last 4 characters.</summary>
public static class VoucherService
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";   // no 0/O/1/I/L/U
    private static readonly ConcurrentDictionary<Guid, (int Fails, DateTime Since)> Fails = new();
    public static string Normalize(string code) => new(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));

    public static string NewCode(string? prefix)
    {
        var chars = new char[16]; for (var i = 0; i < 16; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var body = string.Join('-', Enumerable.Range(0, 4).Select(i => new string(chars, i * 4, 4)));
        return string.IsNullOrWhiteSpace(prefix) ? body : $"{Normalize(prefix)}-{body}";
    }

    private static void Throttle(Guid uid)
    {
        if (Fails.TryGetValue(uid, out var f) && DateTime.UtcNow - f.Since < TimeSpan.FromMinutes(15) && f.Fails >= 10)
            throw new AppException(429, "TOO_MANY_ATTEMPTS", "Too many wrong voucher codes. Try again in a few minutes.");
    }
    private static void Fail(Guid uid) => Fails.AddOrUpdate(uid, _ => (1, DateTime.UtcNow), (_, f) => DateTime.UtcNow - f.Since > TimeSpan.FromMinutes(15) ? (1, DateTime.UtcNow) : (f.Fails + 1, f.Since));

    /// <summary>Finds an unused, unexpired voucher. Every refusal looks the same, so codes cannot be probed for status. Wrong guesses are rate limited per user.</summary>
    public static async Task<(Voucher V, VoucherBatche B)> FindUsableAsync(ApplicationDbContext db, Guid uid, string? code, CancellationToken ct)
    {
        Throttle(uid);
        var bad = new AppException(400, "VOUCHER_INVALID", "This voucher code is not valid.");
        if (string.IsNullOrWhiteSpace(code) || Normalize(code).Length < 8) { Fail(uid); throw bad; }
        var h = Hash(code);
        var v = await db.Vouchers.FirstOrDefaultAsync(x => x.CodeHash == h, ct);
        var b = v is null ? null : await db.VoucherBatches.FirstOrDefaultAsync(x => x.Id == v.VoucherBatchId, ct);
        if (v is null || b is null || v.Status != VoucherStatus.Unused || b.Status != VoucherBatcheStatus.Ready || (b.ExpiresAtUtc is { } e && e <= DateTime.UtcNow)) { Fail(uid); throw bad; }
        return (v, b);
    }

    /// <summary>Single conditional UPDATE: of two parallel redemptions exactly one wins.</summary>
    public static async Task<bool> ClaimAsync(ApplicationDbContext db, Guid voucherId, Guid uid, CancellationToken ct) =>
        await db.Vouchers.Where(v => v.Id == voucherId && v.Status == VoucherStatus.Unused)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VoucherStatus.Redeemed).SetProperty(v => v.RedeemedBy, uid).SetProperty(v => v.RedeemedAtUtc, DateTime.UtcNow), ct) == 1;
}

/// <summary>Wallet, vouchers and manual (offline) payments.</summary>
public sealed class WalletService(ApplicationDbContext db, ICurrentUser me, WalletLedger ledger, CommerceService commerce, AuditWriter audit, Notifier notifier, IOptions<PaymentOptions> opts, IOptions<StorageOptions> storage, IJobQueue queue, BatchGenerator gen)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private string Root => Path.GetFullPath(storage.Value.Root);

    // ---- wallet --------------------------------------------------------------------------------------------------------
    public async Task<object> WalletAsync(CancellationToken ct)
    {
        var w = await ledger.EnsureAsync(Tid, Uid, ct);
        var fresh = await db.Wallets.AsNoTracking().Where(x => x.Id == w.Id).Select(x => new { x.Balance, x.Currency }).FirstAsync(ct);
        return new { balance = fresh.Balance, currency = fresh.Currency };
    }
    public async Task<object> TransactionsAsync(PagedQueryLite q, CancellationToken ct)
    {
        var w = await ledger.EnsureAsync(Tid, Uid, ct);
        var query = db.WalletTransactions.AsNoTracking().Where(t => t.WalletId == w.Id);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.Id).Skip(q.Skip).Take(q.Size).Select(t => new { t.Id, Type = t.Type.ToString(), t.Amount, t.BalanceAfter, t.Description, t.ReferenceType, t.ReferenceId, t.CreatedAtUtc }).ToListAsync(ct);
        return new { items, page = q.Page, pageSize = q.Size, totalCount = total };
    }

    public async Task<object> TopUpVoucherAsync(VoucherCodeRequest r, CancellationToken ct)
    {
        var uid = Uid; var tid = Tid;
        var (v, b) = await VoucherService.FindUsableAsync(db, uid, r.Code, ct);
        if (b.VoucherType != VoucherBatcheVoucherType.WalletCredit || b.WalletAmount is not > 0) throw new AppException(400, "VOUCHER_NOT_APPLICABLE", "This voucher is not a wallet voucher.");
        return await Tx.RunAsync(db, async () =>
        {
            if (!await VoucherService.ClaimAsync(db, v.Id, uid, ct)) throw new AppException(400, "VOUCHER_INVALID", "This voucher code is not valid.");
            await ledger.ApplyAsync(tid, uid, WalletTransactionType.TopUpVoucher, b.WalletAmount.Value, $"voucher:{v.Id}", "Voucher", v.Id, "Voucher top-up", ct);
            var bal = await db.Wallets.AsNoTracking().Where(x => x.UserId == uid).Select(x => x.Balance).FirstAsync(ct);
            return (object)new { credited = b.WalletAmount.Value, balance = bal };
        });
    }

    public async Task<object> RedeemCourseVoucherAsync(VoucherCodeRequest r, CancellationToken ct)
    {
        var uid = Uid; var tid = Tid;
        var (v, b) = await VoucherService.FindUsableAsync(db, uid, r.Code, ct);
        if (b.VoucherType != VoucherBatcheVoucherType.CourseAccess || b.CourseId is null) throw new AppException(400, "VOUCHER_NOT_APPLICABLE", "This voucher is not a course voucher. Use the wallet top-up for wallet vouchers.");
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == b.CourseId && c.Status == CourseStatus.Published, ct) ?? throw new NotFoundException("Course not found.");
        var existing = await db.Enrollments.FirstOrDefaultAsync(e => e.UserId == uid && e.CourseId == course.Id, ct);
        if (existing is not null && existing.Status is EnrollmentStatus.Active or EnrollmentStatus.Completed && (existing.ExpiresAtUtc is null || existing.ExpiresAtUtc > Now)) throw new ConflictAppException("ALREADY_OWNED", "You already have access to this course.");
        await Tx.RunAsync(db, async () =>
        {
            if (!await VoucherService.ClaimAsync(db, v.Id, uid, ct)) throw new AppException(400, "VOUCHER_INVALID", "This voucher code is not valid.");
            await commerce.GrantCourseAsync(tid, uid, course.Id, EnrollmentSource.Voucher, null, null, ct);
            return true;
        });
        return new { courseId = course.Id, title = course.Title };
    }

    // ---- voucher batches -----------------------------------------------------------------------------------------------
    public async Task<object> CreateBatchAsync(VoucherBatchRequest r, CancellationToken ct)
    {
        var tid = Tid; var uid = Uid;
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200) throw new ValidationAppException("A name up to 200 characters is required.");
        if (r.Quantity < 1 || r.Quantity > opts.Value.MaxVouchersPerBatch) throw new ValidationAppException($"Quantity must be between 1 and {opts.Value.MaxVouchersPerBatch}.");
        if (r.Prefix is { Length: > 8 } || (r.Prefix is not null && !r.Prefix.All(char.IsAsciiLetterOrDigit))) throw new ValidationAppException("The prefix must be up to 8 letters or digits.");
        if (r.ExpiresAtUtc is { } ex && ex <= Now) throw new ValidationAppException("The expiry must be in the future.");
        var type = Enum.TryParse<VoucherBatcheVoucherType>(r.VoucherType, true, out var t) && Enum.IsDefined(t) ? t : throw new ValidationAppException("VoucherType must be CourseAccess or WalletCredit.");
        if (type == VoucherBatcheVoucherType.CourseAccess)
        {
            if (r.CourseId is null || !await db.Courses.AnyAsync(c => c.Id == r.CourseId && c.Status == CourseStatus.Published, ct)) throw new NotFoundException("A published course is required.");
            if (r.WalletAmount is not null) throw new ValidationAppException("A course voucher has no wallet amount.");
        }
        else if (r.WalletAmount is not > 0 || r.WalletAmount > 100000 || r.CourseId is not null) throw new ValidationAppException("A wallet voucher needs a WalletAmount between 0 and 100,000 and no course.");
        var batch = new VoucherBatche { TenantId = tid, Name = r.Name.Trim(), Prefix = r.Prefix?.ToUpperInvariant(), Quantity = r.Quantity, VoucherType = type, CourseId = r.CourseId, WalletAmount = r.WalletAmount is null ? null : Tx.R(r.WalletAmount.Value), ExpiresAtUtc = r.ExpiresAtUtc, Status = VoucherBatcheStatus.Pending, CreatedAtUtc = Now };
        var job = new BackgroundJob { TenantId = tid, Type = BackgroundJobType.VoucherGeneration, Status = BackgroundJobStatus.Pending, ReferenceType = "VoucherBatch", ReferenceId = batch.Id, RequestedBy = uid, CreatedAtUtc = Now };
        batch.BackgroundJobId = job.Id;
        db.VoucherBatches.Add(batch); db.BackgroundJobs.Add(job); await db.SaveChangesAsync(ct);
        var hf = queue.Enqueue(job.Id, tid);
        if (hf is null) await gen.RunAsync(job.Id, ct);
        else { job.HangfireJobId = hf; await db.SaveChangesAsync(ct); }
        await audit.WriteAsync(AuditLogAction.Generate, "VoucherBatch", batch.Id, new { batch.Name, batch.Quantity, Type = type.ToString() }, ct);
        var st = await db.BackgroundJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.Status).FirstAsync(ct);
        return new { batchId = batch.Id, jobId = job.Id, status = st.ToString(), queued = hf is not null };
    }

    public async Task<object> BatchesAsync(PagedQueryLite q, CancellationToken ct)
    {
        var total = await db.VoucherBatches.CountAsync(ct);
        var items = await db.VoucherBatches.AsNoTracking().OrderByDescending(b => b.CreatedAtUtc).Skip(q.Skip).Take(q.Size).Select(b => new
        {
            b.Id, b.Name, b.Prefix, b.Quantity, VoucherType = b.VoucherType.ToString(), b.CourseId, b.WalletAmount, b.ExpiresAtUtc, Status = b.Status.ToString(), b.BackgroundJobId, b.CreatedAtUtc,
            unused = db.Vouchers.Count(v => v.VoucherBatchId == b.Id && v.Status == VoucherStatus.Unused), redeemed = db.Vouchers.Count(v => v.VoucherBatchId == b.Id && v.Status == VoucherStatus.Redeemed), voided = db.Vouchers.Count(v => v.VoucherBatchId == b.Id && v.Status == VoucherStatus.Void),
        }).ToListAsync(ct);
        return new { items, page = q.Page, pageSize = q.Size, totalCount = total };
    }

    public async Task<(Stream Content, string Name)> ExportAsync(Guid id, CancellationToken ct)
    {
        var b = await db.VoucherBatches.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Batch not found.");
        if (b.Status != VoucherBatcheStatus.Ready || b.ExportFileId is null) throw new AppException(409, "NOT_READY", "The codes are not generated yet.");
        var file = await db.FileAssets.FirstOrDefaultAsync(f => f.Id == b.ExportFileId, ct);
        var full = file is null ? null : Path.GetFullPath(Path.Combine(Root, file.StoragePath));
        if (file is null || full is null || !full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || file.CreatedAtUtc.AddDays(opts.Value.VoucherExportRetentionDays) < Now || !File.Exists(full))
        {
            if (full is not null && full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(full)) File.Delete(full);
            throw new AppException(410, "EXPORT_EXPIRED", "The code file is no longer kept. Plain codes are not stored anywhere else.");
        }
        await audit.WriteAsync(AuditLogAction.Other, "VoucherBatchExport", b.Id, new { b.Name }, ct);
        return (new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read), $"vouchers-{b.Id:N}.csv");
    }

    public async Task<object> VoidBatchAsync(Guid id, CancellationToken ct)
    {
        var b = await db.VoucherBatches.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Batch not found.");
        var n = await db.Vouchers.Where(v => v.VoucherBatchId == id && v.Status == VoucherStatus.Unused).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VoucherStatus.Void), ct);
        await audit.WriteAsync(AuditLogAction.Update, "VoucherBatch", b.Id, new { action = "void", voided = n }, ct);
        return new { voided = n };
    }

    // ---- manual payments -----------------------------------------------------------------------------------------------
    private static (string Ext, string Type)? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("png", "image/png");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("jpg", "image/jpeg");
        if (b.Length >= 5 && b[..5].SequenceEqual("%PDF-"u8)) return ("pdf", "application/pdf");
        return null;
    }

    public async Task<Guid> SubmitManualAsync(ManualPaymentRequest r, Stream receipt, string originalName, CancellationToken ct)
    {
        var uid = Uid; var tid = Tid;
        var purpose = Enum.TryParse<ManualPaymentSubmissionPurpose>(r.Purpose, true, out var pp) && Enum.IsDefined(pp) ? pp : throw new ValidationAppException("Purpose must be Order or WalletTopUp.");
        var method = Enum.TryParse<ManualPaymentSubmissionMethod>(r.Method, true, out var mm) && Enum.IsDefined(mm) ? mm : throw new ValidationAppException("Method must be VodafoneCash, InstaPay or BankTransfer.");
        var amount = Tx.R(r.Amount);
        if (r.SenderReference is { Length: > 100 } || r.SenderPhoneOrAccount is { Length: > 100 }) throw new ValidationAppException("Sender details are too long.");
        Order? order = null;
        if (purpose == ManualPaymentSubmissionPurpose.Order)
        {
            if (r.OrderId is null) throw new ValidationAppException("OrderId is required for an order payment.");
            order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == r.OrderId && o.UserId == uid, ct) ?? throw new NotFoundException("Order not found.");
            if (order.Status != OrderStatus.AwaitingManualReview || order.PaymentMethod != OrderPaymentMethod.Manual) throw new ConflictAppException("ORDER_NOT_PAYABLE", "This order is not waiting for a manual payment.");
            if (amount != order.Total) throw new AppException(400, "AMOUNT_MISMATCH", $"The transfer must equal the order total of {order.Total:0.00}.");
            if (await db.ManualPaymentSubmissions.AnyAsync(m => m.OrderId == order.Id && m.Status == ManualPaymentSubmissionStatus.Pending, ct)) throw new ConflictAppException("ALREADY_SUBMITTED", "A receipt for this order is already waiting for review.");
        }
        else
        {
            if (r.OrderId is not null) throw new ValidationAppException("A wallet top-up has no order.");
            if (amount < 1 || amount > 50000) throw new ValidationAppException("The top-up must be between 1 and 50,000.");
            if (await db.ManualPaymentSubmissions.CountAsync(m => m.UserId == uid && m.Status == ManualPaymentSubmissionStatus.Pending, ct) >= 5) throw new ConflictAppException("TOO_MANY_PENDING", "You already have 5 receipts waiting for review.");
        }
        var head = new byte[16]; var got = 0; int n;
        while (got < 16 && (n = await receipt.ReadAsync(head.AsMemory(got, 16 - got), ct)) > 0) got += n;
        var kind = Sniff(head.AsSpan(0, got)) ?? throw new ValidationAppException("The receipt must be a PNG, JPEG or PDF file.");
        var rel = Path.Combine("tenants", tid.ToString("N"), "receipts", $"{Guid.NewGuid():N}.{kind.Ext}"); var full = Path.GetFullPath(Path.Combine(Root, rel));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ForbiddenAppException("FORBIDDEN", "Invalid storage path.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        long total = got; using var sha = SHA256.Create(); sha.TransformBlock(head, 0, got, null, 0);
        try
        {
            await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
            {
                await fs.WriteAsync(head.AsMemory(0, got), ct); var buf = new byte[81920];
                while ((n = await receipt.ReadAsync(buf, ct)) > 0) { total += n; if (total > opts.Value.MaxReceiptBytes) throw new ValidationAppException($"The receipt must be {opts.Value.MaxReceiptBytes / 1024 / 1024} MB or smaller."); sha.TransformBlock(buf, 0, n, null, 0); await fs.WriteAsync(buf.AsMemory(0, n), ct); }
            }
            sha.TransformFinalBlock([], 0, 0);
            var safeName = Path.GetFileName(originalName ?? "receipt"); if (safeName.Length > 150) safeName = safeName[..150];
            var asset = new FileAsset { TenantId = tid, OriginalName = safeName, StoragePath = rel, ContentType = kind.Type, SizeBytes = total, Sha256 = Convert.ToHexString(sha.Hash!), UploadedBy = uid, Purpose = FileAssetPurpose.Receipt, CreatedAtUtc = Now };
            db.FileAssets.Add(asset);
            var m = new ManualPaymentSubmission { TenantId = tid, UserId = uid, OrderId = order?.Id, Purpose = purpose, Method = method, Amount = amount, SenderReference = r.SenderReference, SenderPhoneOrAccount = r.SenderPhoneOrAccount, TransferredAtUtc = r.TransferredAtUtc, ReceiptFileId = asset.Id, Status = ManualPaymentSubmissionStatus.Pending, CreatedAtUtc = Now };
            db.ManualPaymentSubmissions.Add(m); await db.SaveChangesAsync(ct);
            return m.Id;
        }
        catch { if (File.Exists(full)) File.Delete(full); throw; }
    }

    public async Task<object> MineManualAsync(CancellationToken ct) =>
        await db.ManualPaymentSubmissions.AsNoTracking().Where(m => m.UserId == Uid).OrderByDescending(m => m.CreatedAtUtc).Take(100).Select(m => new { m.Id, Purpose = m.Purpose.ToString(), Method = m.Method.ToString(), m.Amount, m.OrderId, Status = m.Status.ToString(), m.RejectionReason, m.CreatedAtUtc, m.ReviewedAtUtc }).ToListAsync(ct);

    public async Task<object> ReviewQueueAsync(string? status, PagedQueryLite q, CancellationToken ct)
    {
        var st = string.IsNullOrWhiteSpace(status) ? ManualPaymentSubmissionStatus.Pending : Enum.TryParse<ManualPaymentSubmissionStatus>(status, true, out var s) ? s : throw new ValidationAppException("Unknown status.");
        var query = db.ManualPaymentSubmissions.AsNoTracking().Where(m => m.Status == st);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(m => m.CreatedAtUtc).Skip(q.Skip).Take(q.Size).Select(m => new { m.Id, m.UserId, userName = db.Users.Where(u => u.Id == m.UserId).Select(u => u.FullName).FirstOrDefault(), Purpose = m.Purpose.ToString(), Method = m.Method.ToString(), m.Amount, m.OrderId, m.SenderReference, m.SenderPhoneOrAccount, m.TransferredAtUtc, Status = m.Status.ToString(), m.CreatedAtUtc }).ToListAsync(ct);
        return new { items, page = q.Page, pageSize = q.Size, totalCount = total };
    }

    public async Task<(Stream Content, string Type, string Name)> ReceiptAsync(Guid id, CancellationToken ct)
    {
        var m = await db.ManualPaymentSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Submission not found.");
        var staff = me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
        if (m.UserId != Uid && !staff) throw new NotFoundException("Submission not found.");
        var f = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == m.ReceiptFileId, ct) ?? throw new NotFoundException("Receipt not found.");
        var full = Path.GetFullPath(Path.Combine(Root, f.StoragePath));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full)) throw new NotFoundException("Receipt not found.");
        return (new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read), f.ContentType, f.OriginalName);
    }

    public async Task ApproveManualAsync(Guid id, CancellationToken ct)
    {
        var m = await db.ManualPaymentSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Submission not found.");
        var reviewer = Uid;
        await Tx.RunAsync(db, async () =>
        {
            var won = await db.ManualPaymentSubmissions.Where(x => x.Id == id && x.Status == ManualPaymentSubmissionStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ManualPaymentSubmissionStatus.Approved).SetProperty(x => x.ReviewedBy, reviewer).SetProperty(x => x.ReviewedAtUtc, Now), ct);
            if (won == 0) throw new ConflictAppException("ALREADY_REVIEWED", "This submission was already reviewed.");
            if (m.Purpose == ManualPaymentSubmissionPurpose.Order)
            {
                var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == m.OrderId, ct) ?? throw new NotFoundException("Order not found.");
                if (o.Status != OrderStatus.AwaitingManualReview || Tx.R(o.Total) != m.Amount) throw new ConflictAppException("ORDER_NOT_PAYABLE", "The order is no longer waiting for this payment.");
                db.Payments.Add(new Payment { TenantId = m.TenantId, OrderId = o.Id, Method = PaymentMethod.Manual, Amount = o.Total, Currency = o.Currency, Status = PaymentStatus.Succeeded, CreatedAtUtc = Now, ConfirmedAtUtc = Now, IdempotencyKey = $"manual:{id}" });
                await db.SaveChangesAsync(ct);
                await commerce.FulfillOrderAsync(o.Id, EnrollmentSource.Purchase, ct);
            }
            else
            {
                await ledger.ApplyAsync(m.TenantId, m.UserId, WalletTransactionType.TopUpManual, m.Amount, $"manual:{id}", "ManualPayment", id, "Manual top-up", ct);
                var tx = await db.WalletTransactions.AsNoTracking().Where(t => t.IdempotencyKey == $"manual:{id}").Select(t => t.Id).FirstAsync(ct);
                await db.ManualPaymentSubmissions.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.WalletTransactionId, tx), ct);
            }
            await audit.WriteAsync(AuditLogAction.Approve, "ManualPaymentSubmission", id, new { m.Amount, m.UserId, Purpose = m.Purpose.ToString() }, ct);
            return true;
        });
        await notifier.SendAsync(m.TenantId, [m.UserId], "PaymentApproved", "Payment approved", $"Your transfer of {m.Amount:0.00} was approved.", ct);
    }

    public async Task RejectManualAsync(Guid id, RejectPaymentRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500) throw new ValidationAppException("A reason up to 500 characters is required.");
        var m = await db.ManualPaymentSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Submission not found.");
        var reviewer = Uid; var reason = r.Reason.Trim();
        var won = await db.ManualPaymentSubmissions.Where(x => x.Id == id && x.Status == ManualPaymentSubmissionStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ManualPaymentSubmissionStatus.Rejected).SetProperty(x => x.ReviewedBy, reviewer).SetProperty(x => x.ReviewedAtUtc, Now).SetProperty(x => x.RejectionReason, reason), ct);
        if (won == 0) throw new ConflictAppException("ALREADY_REVIEWED", "This submission was already reviewed.");
        await audit.WriteAsync(AuditLogAction.Reject, "ManualPaymentSubmission", id, new { reason }, ct);
        await notifier.SendAsync(m.TenantId, [m.UserId], "PaymentRejected", "Payment rejected", reason, ct);
    }
}

/// <summary>Generates the codes of one voucher batch. Plain codes exist only in the exported CSV; the database keeps hashes.</summary>
public sealed class BatchGenerator(ApplicationDbContext db, IOptions<StorageOptions> storage)
{
    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId, ct);
        var batch = await db.VoucherBatches.FirstOrDefaultAsync(b => b.Id == job.ReferenceId, ct);
        if (batch is null || batch.Status == VoucherBatcheStatus.Ready) { job.Status = BackgroundJobStatus.Completed; job.ProgressPercent = 100; job.CompletedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return; }
        job.Status = BackgroundJobStatus.Processing; job.StartedAtUtc = DateTime.UtcNow; batch.Status = VoucherBatcheStatus.Generating; await db.SaveChangesAsync(ct);
        try
        {
            // A retry starts clean: the plain codes of an earlier half-finished run are gone, so its hashes would be unusable.
            await db.Vouchers.Where(v => v.VoucherBatchId == batch.Id).ExecuteDeleteAsync(ct);
            var csv = new StringBuilder("code,type,course_id,wallet_amount,expires_at_utc\n");
            var made = 0;
            while (made < batch.Quantity)
            {
                var chunk = Math.Min(500, batch.Quantity - made); var rows = new List<(string Code, Voucher V)>();
                for (var i = 0; i < chunk; i++)
                {
                    var code = VoucherService.NewCode(batch.Prefix);
                    rows.Add((code, new Voucher { TenantId = batch.TenantId, VoucherBatchId = batch.Id, CodeHash = VoucherService.Hash(code), CodeLast4 = VoucherService.Normalize(code)[^4..], Status = VoucherStatus.Unused, CreatedAtUtc = DateTime.UtcNow }));
                }
                if (rows.Select(r => r.V.CodeHash).Distinct().Count() != rows.Count) continue;   // astronomically unlikely clash inside a chunk
                db.Vouchers.AddRange(rows.Select(r => r.V));
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId, ct); batch = await db.VoucherBatches.FirstAsync(b => b.Id == batch.Id, ct); continue; }   // clash with an older code: draw a fresh chunk
                foreach (var (code, _) in rows) csv.Append(code).Append(',').Append(batch.VoucherType).Append(',').Append(batch.CourseId).Append(',').Append(batch.WalletAmount).Append(',').Append(batch.ExpiresAtUtc?.ToString("O")).Append('\n');
                made += chunk; job.ProgressPercent = made * 90 / batch.Quantity; await db.SaveChangesAsync(ct);
            }
            var root = Path.GetFullPath(storage.Value.Root);
            var rel = Path.Combine("tenants", batch.TenantId.ToString("N"), "exports", $"{Guid.NewGuid():N}.csv"); var full = Path.GetFullPath(Path.Combine(root, rel));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var bytes = Encoding.UTF8.GetBytes(csv.ToString()); await File.WriteAllBytesAsync(full, bytes, ct);
            var asset = new FileAsset { TenantId = batch.TenantId, OriginalName = $"vouchers-{batch.Id:N}.csv", StoragePath = rel, ContentType = "text/csv", SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), UploadedBy = job.RequestedBy, Purpose = FileAssetPurpose.Export, CreatedAtUtc = DateTime.UtcNow };
            db.FileAssets.Add(asset); batch.ExportFileId = asset.Id; batch.Status = VoucherBatcheStatus.Ready;
            job.Status = BackgroundJobStatus.Completed; job.ProgressPercent = 100; job.CompletedAtUtc = DateTime.UtcNow; job.ResultJson = $"{{\"generated\":{batch.Quantity}}}";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            await db.VoucherBatches.Where(b => b.Id == batch.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, VoucherBatcheStatus.Failed), ct);
            await db.BackgroundJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BackgroundJobStatus.Failed).SetProperty(j => j.ErrorMessage, "Voucher generation failed.").SetProperty(j => j.CompletedAtUtc, DateTime.UtcNow), ct);
        }
    }
}
