using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.Infrastructure.Services.Commerce;

public sealed record AffiliateProfileRequest(Guid UserId, double CommissionPercent, string? ReferralCode);
public sealed record PayoutCalcRequest(DateTime PeriodStartUtc, DateTime PeriodEndUtc);
public sealed record PayoutUpdateRequest(string Status, string? PaymentReference);

/// <summary>Affiliate profiles, instructor revenue shares and payouts.</summary>
public sealed class RevenueService(ApplicationDbContext db, ICurrentUser me, IOptions<PaymentOptions> opts, IJobQueue queue, AuditWriter audit, Notifier notifier)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");

    // ---- affiliate -----------------------------------------------------------------------------------------------------
    public async Task<object> AffiliateMeAsync(CancellationToken ct)
    {
        var uid = Uid;
        var p = await db.AffiliateProfiles.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == uid && a.IsActive, ct) ?? throw new AppException(404, "NOT_AN_AFFILIATE", "You are not an affiliate. Ask an administrator to enable it.");
        var refs = await (from r in db.Referrals.AsNoTracking() where r.AffiliateUserId == uid join u in db.Users on r.ReferredUserId equals u.Id orderby r.CreatedAtUtc descending select new { r.ReferredUserId, name = u.FullName, r.CreatedAtUtc }).Take(200).ToListAsync(ct);
        var coms = await db.AffiliateCommissions.AsNoTracking().Where(c => c.AffiliateUserId == uid).OrderByDescending(c => c.CreatedAtUtc).Take(200).Select(c => new { c.Id, c.OrderId, c.Amount, Status = c.Status.ToString(), c.CreatedAtUtc }).ToListAsync(ct);
        var total = await db.AffiliateCommissions.Where(c => c.AffiliateUserId == uid && c.Status == AffiliateCommissionStatus.Credited).SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
        return new { referralCode = p.ReferralCode, referralLink = $"/register?ref={Uri.EscapeDataString(p.ReferralCode)}", commissionPercent = p.CommissionPercent, referrals = refs.Select(r => new { r.ReferredUserId, name = FirstName(r.name), r.CreatedAtUtc }), commissions = coms, totalCredited = total };
    }
    private static string FirstName(string n) => n.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    public async Task<object> CreateAffiliateAsync(AffiliateProfileRequest r, CancellationToken ct)
    {
        if (r.CommissionPercent is <= 0 or > 50) throw new ValidationAppException("CommissionPercent must be above 0 and at most 50.");
        if (!await db.Users.AnyAsync(u => u.Id == r.UserId && u.TenantId == Tid && u.IsActive, ct)) throw new NotFoundException("User not found.");
        var existing = await db.AffiliateProfiles.FirstOrDefaultAsync(a => a.UserId == r.UserId, ct);
        if (existing is not null) { existing.CommissionPercent = r.CommissionPercent; existing.IsActive = true; await db.SaveChangesAsync(ct); return new { existing.UserId, referralCode = existing.ReferralCode, commissionPercent = existing.CommissionPercent }; }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = string.IsNullOrWhiteSpace(r.ReferralCode) ? "R" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)) : r.ReferralCode.Trim().ToUpperInvariant();
            if (code.Length is < 4 or > 20 || !code.All(char.IsAsciiLetterOrDigit)) throw new ValidationAppException("The referral code must be 4 to 20 letters or digits.");
            var p = new AffiliateProfile { TenantId = Tid, UserId = r.UserId, ReferralCode = code, CommissionPercent = r.CommissionPercent, CreatedAtUtc = Now };
            db.AffiliateProfiles.Add(p);
            try { await db.SaveChangesAsync(ct); await audit.WriteAsync(AuditLogAction.Create, "AffiliateProfile", p.Id, new { r.UserId, r.CommissionPercent }, ct); return new { p.UserId, referralCode = p.ReferralCode, commissionPercent = p.CommissionPercent }; }
            catch (DbUpdateException) { db.Entry(p).State = EntityState.Detached; if (!string.IsNullOrWhiteSpace(r.ReferralCode)) throw new ConflictAppException("CODE_TAKEN", "This referral code is taken."); }
        }
        throw new ConflictAppException("CODE_TAKEN", "Could not allocate a referral code.");
    }

    // ---- instructor revenue --------------------------------------------------------------------------------------------
    private sealed record Row(Guid CourseId, Guid InstructorId, decimal Net, double Share, string Kind = "course", long Seconds = 0, int Lessons = 0, Guid Item = default, Guid Order = default, decimal GrossNet = 0);
    private sealed record HeldLine(Guid OrderItemId, Guid CourseId, Guid InstructorId, decimal Net, string Kind);
    private sealed record Computation(List<Row> Rows, decimal UnallocatedSubscription, decimal HeldNoFrozenShare = 0, List<HeldLine>? HeldLines = null);

    private static IEnumerable<(Guid Id, decimal Amount)> Split(decimal amount, IReadOnlyList<(Guid Id, decimal Weight)> parts)
    {
        if (parts.Count == 0) yield break;
        var total = parts.Sum(p => p.Weight); var left = amount;
        for (var i = 0; i < parts.Count; i++)
        {
            var v = i == parts.Count - 1 ? left : Tx.R(total > 0 ? amount * parts[i].Weight / total : amount / parts.Count);
            left -= v; yield return (parts[i].Id, v);
        }
    }

    /// <summary>
    /// Earnings basis for a period, by approved policy: course and lesson sales are attributed to the course; bundles and learning paths are split across their
    /// courses by list price (equal split when every price is 0); a subscription's revenue is recognised when its paid window ends and is split across the
    /// courses the subscriber actually learned in that window by qualified learning time (see QualifiedSeconds). Then the course's instructor share applies.
    /// Fees, coupons' cost to the academy, affiliate commission and refunds are NOT deducted (policy not decided).
    /// </summary>
    private async Task<Computation> ComputeAsync(DateTime fromUtc, DateTime toUtc, Guid? instructor, CancellationToken ct, Guid? onlyOrder = null, bool applyRefunds = true, Guid? onlyItem = null, bool settleLate = false)
    {
        var tenant = await db.Tenants.AsNoTracking().Where(x => x.Id == Tid).Select(x => new { x.InstructorSharePercent, x.SubscriptionMinWatchPercent }).FirstOrDefaultAsync(ct);
        var alloc = new List<(Guid Course, decimal Net, string Kind, long Secs, int Lessons, Guid Item)>(); decimal unallocated = 0;
        var lines = await (from i in db.OrderItems.AsNoTracking()
                           join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id
                           where o.Status == OrderStatus.Paid && o.PaidAtUtc >= fromUtc && o.PaidAtUtc < toUtc && (onlyOrder == null || o.Id == onlyOrder) && (onlyItem == null || i.Id == onlyItem) && (i.ItemType == OrderItemItemType.Course || i.ItemType == OrderItemItemType.Lesson || i.ItemType == OrderItemItemType.Bundle || i.ItemType == OrderItemItemType.Path)
                           select new { OrderId = o.Id, i.Id, i.ItemType, i.CourseId, i.LessonId, i.BundleId, i.LearningPathId, i.LineTotal, o.Subtotal, o.Total }).ToListAsync(ct);
        var lessonIds = lines.Where(x => x.LessonId != null).Select(x => x.LessonId!.Value).ToList();
        var lessonCourse = await db.Lessons.AsNoTracking().Where(l => lessonIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.CourseId, ct);
        var multiIds = lines.Where(x => x.ItemType is OrderItemItemType.Bundle or OrderItemItemType.Path).Select(x => x.Id).ToList();
        var snapshots = (await db.OrderItemAllocations.AsNoTracking().Where(x => multiIds.Contains(x.OrderItemId)).Select(x => new { x.OrderItemId, x.CourseId, x.ListPrice }).ToListAsync(ct)).GroupBy(x => x.OrderItemId).ToDictionary(g => g.Key, g => g.Select(x => (x.CourseId, x.ListPrice)).ToList());
        foreach (var l in lines)
        {
            var net = l.Subtotal > 0 ? Tx.R(l.LineTotal * l.Total / l.Subtotal) : 0;
            if (l.ItemType is OrderItemItemType.Course or OrderItemItemType.Lesson)
            {
                var cid = l.CourseId ?? (l.LessonId is Guid lid && lessonCourse.TryGetValue(lid, out var c) ? c : Guid.Empty);
                if (cid != Guid.Empty) alloc.Add((cid, net, "course", 0, 0, l.Id));
                continue;
            }
            // weights are the list prices frozen at checkout, so later price edits or bundle edits never move money already earned
            if (!snapshots.TryGetValue(l.Id, out var snap) || snap.Count == 0) { unallocated += net; continue; }
            foreach (var (cid, amt) in Split(net, snap.Select(x => (x.CourseId, x.ListPrice)).ToList())) alloc.Add((cid, amt, "bundle", 0, 0, l.Id));
        }

        // ---- subscriptions: recognised when the paid window ends inside the period --------------------------------------------------------
        var planLines = await (from i in db.OrderItems.AsNoTracking()
                               join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id
                               where o.Status == OrderStatus.Paid && i.ItemType == OrderItemItemType.Plan && o.PaidAtUtc != null && (onlyOrder == null || o.Id == onlyOrder) && (onlyItem == null || i.Id == onlyItem)
                               select new { OrderId = o.Id, ItemId = i.Id, i.PlanId, o.UserId, Paid = o.PaidAtUtc!.Value, i.LineTotal, o.Subtotal, o.Total }).ToListAsync(ct);
        var plans = await db.SubscriptionPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tid).ToDictionaryAsync(x => x.Id, ct);
        DateTime EndOf(DateTime paid, Guid? planId) => !(planId is Guid pid && plans.TryGetValue(pid, out var pl)) ? paid : pl.BillingPeriod switch { SubscriptionPlanBillingPeriod.Monthly => paid.AddMonths(1), SubscriptionPlanBillingPeriod.Quarterly => paid.AddMonths(3), _ => paid.AddYears(1) };
        // Revenue is earned evenly over the paid window (cumulative rounding, so the periods add up to the exact amount). A cancellation only stops renewal, access and
        // the paid window run to their end, so the full window is recognised. Each period's share is split by qualified learning inside that period.
        var live = planLines.Select(x => (x, Start: x.Paid, End: EndOf(x.Paid, x.PlanId))).Where(x => x.End > x.Start && x.Start < toUtc && x.End > fromUtc).ToList();
        if (live.Count > 0)
        {
            if (tenant?.SubscriptionMinWatchPercent is not double minWatch) throw new AppException(409, "SUBSCRIPTION_ALLOCATION_NOT_CONFIGURED", "Subscription revenue cannot be allocated until the minimum watch percentage is configured by the academy administrator.");
            var selected = (await db.SubscriptionPlanCourses.AsNoTracking().Select(x => new { x.PlanId, x.CourseId }).ToListAsync(ct)).GroupBy(x => x.PlanId).ToDictionary(g => g.Key, g => g.Select(x => x.CourseId).ToHashSet());
            var userIds = live.Select(x => x.x.UserId).Distinct().ToList();
            var ownedItems = await (from i in db.OrderItems.AsNoTracking() join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id
                                    where o.Status == OrderStatus.Paid && userIds.Contains(o.UserId) && (i.ItemType == OrderItemItemType.Course || i.ItemType == OrderItemItemType.Bundle || i.ItemType == OrderItemItemType.Path)
                                    select new { o.UserId, i.Id, i.CourseId, i.BundleId, i.LearningPathId }).ToListAsync(ct);
            var ownedIds = ownedItems.Select(x => x.Id).ToList();
            var ownedSnap = (await db.OrderItemAllocations.AsNoTracking().Where(x => ownedIds.Contains(x.OrderItemId)).Select(x => new { x.OrderItemId, x.CourseId }).ToListAsync(ct)).GroupBy(x => x.OrderItemId).ToDictionary(g => g.Key, g => g.Select(x => x.CourseId).ToList());
            var owned = ownedItems.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.SelectMany(x => x.CourseId is Guid c ? [c] : ownedSnap.GetValueOrDefault(x.Id) ?? new List<Guid>()).ToHashSet());
            var planItemIds = live.Select(x => x.x.ItemId).ToList();
            var planSnap = (await db.OrderItemAllocations.AsNoTracking().Where(x => planItemIds.Contains(x.OrderItemId)).Select(x => new { x.OrderItemId, x.CourseId, x.NonVideoSeconds, x.NonVideoLessonCount }).ToListAsync(ct)).GroupBy(x => x.OrderItemId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.CourseId, x => (Secs: x.NonVideoSeconds ?? 0, Count: x.NonVideoLessonCount ?? 0)));
            var raw = await (from pr in db.LessonProgress.AsNoTracking() join le in db.Lessons.AsNoTracking() on pr.LessonId equals le.Id
                             join f in db.FileAssets.AsNoTracking() on le.ContentFileId equals f.Id into fj from f in fj.DefaultIfEmpty()
                             where userIds.Contains(pr.UserId) && pr.Status == LessonProgressStatus.Completed && pr.CompletedAtUtc != null && !le.IsFreePreview
                             select new { pr.UserId, pr.LessonId, le.CourseId, le.LessonType, Sha = f == null ? null : f.Sha256, Measured = f == null ? null : f.MeasuredDurationSeconds, pr.WatchedSeconds, Done = pr.CompletedAtUtc!.Value }).ToListAsync(ct);
            // Lifetime de-duplication, independent of the period: the same media bytes (same hash) count once per student whatever lesson id or course they were re-uploaded under, and
            // the k-th non-video completion of a course earns weight/total only while k <= total, so re-completion, re-upload and splitting add nothing beyond the course's approved weight.
            var credits = new List<(Guid User, Guid Course, DateTime Done, long Secs)>();
            foreach (var g in raw.Where(x => x.LessonType is LessonType.Video or LessonType.Audio).GroupBy(x => (x.UserId, Key: x.Sha ?? x.LessonId.ToString())))
            {
                var first = g.OrderBy(x => x.Done).First();
                var q = first.Measured is int d && d > 0 && first.WatchedSeconds >= d * minWatch / 100.0 ? Math.Min(first.WatchedSeconds, d) : 0;
                if (q > 0) credits.Add((first.UserId, first.CourseId, first.Done, q));
            }
            foreach (var (pl, start, end) in live)
            {
                var net = pl.Subtotal > 0 ? Tx.R(pl.LineTotal * pl.Total / pl.Subtotal) : 0;
                var a = start > fromUtc ? start : fromUtc; var b = end < toUtc ? end : toUtc; if (b <= a) continue;
                double Frac(DateTime t) => Math.Clamp((t - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);
                var recognized = Tx.R(net * (decimal)Frac(b)) - Tx.R(net * (decimal)Frac(a));
                if (recognized <= 0) continue;
                var cover = pl.PlanId is Guid pid && plans.TryGetValue(pid, out var plan) && plan.Scope == SubscriptionPlanScope.SelectedCourses ? selected.GetValueOrDefault(pid) ?? [] : null;
                var mine = owned.GetValueOrDefault(pl.UserId) ?? [];
                // non-video learning uses the allowance frozen on this plan line: the k-th lifetime completion earns weight/count while k <= count
                var lineCredits = new List<(Guid User, Guid Course, DateTime Done, long Secs)>();
                if (planSnap.TryGetValue(pl.ItemId, out var snap))
                    foreach (var g in raw.Where(x => x.UserId == pl.UserId && x.LessonType is not (LessonType.Video or LessonType.Audio) && snap.ContainsKey(x.CourseId)).GroupBy(x => x.CourseId))
                    {
                        var (w, n) = snap[g.Key]; if (w <= 0 || n <= 0) continue; var k = 0;
                        foreach (var x in g.OrderBy(x => x.Done)) { if (++k > n) break; if (x.Done >= a && x.Done < b) lineCredits.Add((x.UserId, x.CourseId, x.Done, (long)Math.Round((double)w / n))); }
                    }
                var per = credits.Concat(lineCredits).Where(c => c.User == pl.UserId && c.Done >= a && c.Done < b && (cover is null || cover.Contains(c.Course)) && !mine.Contains(c.Course)).GroupBy(c => c.Course).OrderBy(g => g.Key).Select(g => (Course: g.Key, Secs: g.Sum(x => x.Secs), Lessons: g.Count())).ToList();
                var totalSecs = per.Sum(x => x.Secs);
                if (totalSecs == 0) { unallocated += recognized; continue; }
                var left = recognized;
                for (var i = 0; i < per.Count; i++)
                {
                    var v = i == per.Count - 1 ? left : Tx.R(recognized * per[i].Secs / totalSecs); left -= v;
                    alloc.Add((per[i].Course, v, "subscription", per[i].Secs, per[i].Lessons, pl.ItemId));
                }
            }
        }

        // Refunds recorded for an order scale every line of it by the refunded share (accounting policy approved by the user); gross when asked for the debit calculation.
        var itemOrder = lines.ToDictionary(x => x.Id, x => x.OrderId); foreach (var x in planLines) itemOrder[x.ItemId] = x.OrderId;
        var orderIds = itemOrder.Values.Distinct().ToList(); var keep = new Dictionary<Guid, decimal>();
        if (applyRefunds && orderIds.Count > 0)
        {
            var refunded = await db.OrderRefunds.AsNoTracking().Where(x => orderIds.Contains(x.OrderId)).GroupBy(x => x.OrderId).Select(g => new { Id = g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
            var totals = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Total, ct);
            foreach (var r in refunded) keep[r.Id] = totals.TryGetValue(r.Id, out var tot) && tot > 0 ? Math.Max(0m, 1m - r.Sum / tot) : 1m;
        }
        decimal Keep(Guid item) => itemOrder.TryGetValue(item, out var o) && keep.TryGetValue(o, out var k) ? k : 1m;
        var courseIds = alloc.Select(a => a.Course).Distinct().ToList();
        var itemIds = alloc.Select(a => a.Item).Distinct().ToList();
        var owners = await db.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).Select(c => new { c.Id, c.OwnerInstructorId }).ToDictionaryAsync(c => c.Id, c => c.OwnerInstructorId, ct);
        var current = await db.InstructorRevenueShares.AsNoTracking().Where(s => courseIds.Contains(s.CourseId)).ToListAsync(ct);
        var frozen = (await db.OrderItemShares.AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId)).Select(x => new { x.OrderItemId, x.CourseId, x.InstructorId, x.SharePercent }).ToListAsync(ct)).GroupBy(x => (x.OrderItemId, x.CourseId)).ToDictionary(g => g.Key, g => g.Select(x => (x.InstructorId, x.SharePercent)).ToList());
        double? Current(Guid course, Guid instr) => current.Where(x => x.CourseId == course && x.InstructorId == instr).Select(x => (double?)x.SharePercent).FirstOrDefault() ?? (current.Any(x => x.CourseId == course) ? null : tenant?.InstructorSharePercent);
        // releases made after a payout already covered the sale are settled in a LATER payout (settleLate); until then, and in every other calculation, they stay out
        var lateSet = (await db.OrderItemShares.AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId) && x.ReleasedLate && x.SettledPayoutId == null).Select(x => new { x.OrderItemId, x.CourseId, x.InstructorId }).ToListAsync(ct)).Select(x => (x.OrderItemId, x.CourseId, x.InstructorId)).ToHashSet();
        var rows = new List<Row>(); decimal held = 0; var heldLines = new List<HeldLine>();
        foreach (var a in alloc)
        {
            if (!owners.TryGetValue(a.Course, out var owner)) continue;
            // Only percentages recorded at purchase are ever used. A line/course with no recorded percentage (none configured then, or the course was added to a plan afterwards) is HELD: never computed from today's setting, so old purchases cannot change.
            if (!frozen.TryGetValue((a.Item, a.Course), out var parts) || parts.Count == 0) { held += a.Net * Keep(a.Item); heldLines.Add(new HeldLine(a.Item, a.Course, owner, Tx.R(a.Net * Keep(a.Item)), a.Kind)); continue; }
            foreach (var (instr, pct) in parts)
            {
                var isLate = lateSet.Contains((a.Item, a.Course, instr)); if (isLate != settleLate) continue;
                if (pct is null) { held += a.Net * Keep(a.Item) / parts.Count; heldLines.Add(new HeldLine(a.Item, a.Course, instr, Tx.R(a.Net * Keep(a.Item) / parts.Count), a.Kind)); continue; }
                if (instructor is null || instr == instructor) rows.Add(new Row(a.Course, instr, a.Net * Keep(a.Item), pct.Value, a.Kind, a.Secs, a.Lessons, a.Item, itemOrder.GetValueOrDefault(a.Item), a.Net));
            }
        }
        return new Computation(rows, unallocated, Tx.R(held), heldLines);
    }

    /// <summary>The statement a TenantAdmin reviews before approving payouts: per instructor the earnings by source, the qualified learning minutes behind the subscription part, and anomaly flags.</summary>
    public async Task<object> StatementAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(366)) throw new ValidationAppException("The period must end after it starts and span at most one year.");
        var cur = await ComputeAsync(fromUtc, toUtc, null, ct);
        var prev = await ComputeAsync(fromUtc - (toUtc - fromUtc), fromUtc, null, ct);
        var t = await db.Tenants.AsNoTracking().Where(x => x.Id == Tid).Select(x => new { x.SubscriptionMinAvgLessonSeconds, x.SubscriptionJumpAlertPercent }).FirstAsync(ct);
        decimal Earn(IEnumerable<Row> r) => Tx.R(r.Sum(x => x.Net * (decimal)x.Share / 100m));
        var names = await db.Users.AsNoTracking().Where(u => cur.Rows.Select(r => r.InstructorId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var instructors = cur.Rows.GroupBy(r => r.InstructorId).Select(g =>
        {
            var sub = g.Where(r => r.Kind == "subscription").ToList(); var secs = sub.Sum(r => r.Seconds); var lessons = sub.Sum(r => r.Lessons);
            var subEarn = Earn(sub); var prevSub = Earn(prev.Rows.Where(r => r.InstructorId == g.Key && r.Kind == "subscription"));
            var flags = new List<string>();
            if (t.SubscriptionMinAvgLessonSeconds is int minAvg && lessons > 0 && secs / lessons < minAvg) flags.Add("SHORT_LESSONS");
            if (t.SubscriptionJumpAlertPercent is double j && prevSub > 0 && subEarn > prevSub * (1 + (decimal)j / 100m)) flags.Add("SUBSCRIPTION_EARNINGS_JUMP");
            return new { instructorId = g.Key, name = names.GetValueOrDefault(g.Key, ""), total = Earn(g), courseSales = Earn(g.Where(r => r.Kind == "course")), bundleAndPath = Earn(g.Where(r => r.Kind == "bundle")), subscription = subEarn,
                subscriptionQualifiedMinutes = Math.Round(secs / 60.0, 1), subscriptionLessonsCounted = lessons, previousPeriodSubscription = prevSub, flags };
        }).OrderByDescending(x => x.total).ToList();
        return new { fromUtc, toUtc, instructors, unallocatedSubscriptionRevenue = cur.UnallocatedSubscription, heldNoRecordedShare = cur.HeldNoFrozenShare, note = "Fees, refunds and affiliate commission are not deducted." };
    }

    public sealed record SubscriptionControlsRequest(double? MinWatchPercent, int? MinAverageLessonSeconds, double? JumpAlertPercent);
    public async Task<object> GetSubscriptionControlsAsync(CancellationToken ct) =>
        await db.Tenants.AsNoTracking().Where(x => x.Id == Tid).Select(x => new { minWatchPercent = x.SubscriptionMinWatchPercent, minAverageLessonSeconds = x.SubscriptionMinAvgLessonSeconds, jumpAlertPercent = x.SubscriptionJumpAlertPercent }).FirstAsync(ct);
    public async Task<object> SetSubscriptionControlsAsync(SubscriptionControlsRequest r, CancellationToken ct)
    {
        if (r.MinWatchPercent is double w && (double.IsNaN(w) || w < 0 || w > 100)) throw new ValidationAppException("Minimum watch percentage must be between 0 and 100.");
        if (r.MinAverageLessonSeconds is < 0) throw new ValidationAppException("Minimum average lesson length cannot be negative.");
        if (r.JumpAlertPercent is double j && (double.IsNaN(j) || j < 0)) throw new ValidationAppException("The jump alert percentage cannot be negative.");
        var t = await db.Tenants.FirstAsync(x => x.Id == Tid, ct);
        var old = new { t.SubscriptionMinWatchPercent, t.SubscriptionMinAvgLessonSeconds, t.SubscriptionJumpAlertPercent };
        t.SubscriptionMinWatchPercent = r.MinWatchPercent; t.SubscriptionMinAvgLessonSeconds = r.MinAverageLessonSeconds; t.SubscriptionJumpAlertPercent = r.JumpAlertPercent;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Update, "SubscriptionAllocationControls", t.Id, new { old, @new = r }, ct);
        return await GetSubscriptionControlsAsync(ct);
    }
    /// <summary>GET /courses/{id}/learning-weight: the current non-video learning weight (null when never set), so the form can prefill instead of overwriting blind.</summary>
    public async Task<object> GetLearningWeightAsync(Guid courseId, CancellationToken ct)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == courseId, ct)) throw new NotFoundException("Course not found.");
        var w = await db.CourseLearningWeights.AsNoTracking().FirstOrDefaultAsync(x => x.CourseId == courseId, ct);
        return new { courseId, nonVideoCourseSeconds = w?.NonVideoCourseSeconds };
    }
    public sealed record LearningWeightRequest(int NonVideoCourseSeconds);
    /// <summary>TenantAdmin approves how much learning time ALL the non-video content of a course is worth together. It is shared over the course's non-video lessons, so adding or splitting lessons never multiplies it.</summary>
    public async Task<object> SetLearningWeightAsync(Guid courseId, LearningWeightRequest r, CancellationToken ct)
    {
        if (r.NonVideoCourseSeconds < 0 || r.NonVideoCourseSeconds > 100 * 3600) throw new ValidationAppException("The weight must be between 0 and 100 hours.");
        if (!await db.Courses.AnyAsync(c => c.Id == courseId, ct)) throw new NotFoundException("Course not found.");
        var w = await db.CourseLearningWeights.FirstOrDefaultAsync(x => x.CourseId == courseId, ct); int? old = w?.NonVideoCourseSeconds;
        if (w is null) db.CourseLearningWeights.Add(new CourseLearningWeight { TenantId = Tid, CourseId = courseId, NonVideoCourseSeconds = r.NonVideoCourseSeconds, CreatedAtUtc = Now });
        else { w.NonVideoCourseSeconds = r.NonVideoCourseSeconds; w.UpdatedAtUtc = Now; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(w is null ? AuditLogAction.Create : AuditLogAction.Update, "CourseLearningWeight", courseId, new { old, @new = r.NonVideoCourseSeconds }, ct);
        return new { courseId, r.NonVideoCourseSeconds };
    }

    /// <summary>TenantAdmin. The academy's default instructor share (null clears it). Course/instructor rows still win. Already calculated payouts keep their stored amount.</summary>
    public async Task<object> SetDefaultShareAsync(double? percent, CancellationToken ct)
    {
        if (percent is double d && (double.IsNaN(d) || d < 0 || d > 100)) throw new ValidationAppException("The percentage must be between 0 and 100.");
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == Tid, ct) ?? throw new NotFoundException("Tenant not found.");
        var old = t.InstructorSharePercent; t.InstructorSharePercent = percent; await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Update, "TenantInstructorShare", t.Id, new { old, percent }, ct);
        return new { instructorSharePercent = t.InstructorSharePercent };
    }
    public async Task<object> GetSharesAsync(CancellationToken ct)
    {
        var def = await db.Tenants.AsNoTracking().Where(x => x.Id == Tid).Select(x => x.InstructorSharePercent).FirstOrDefaultAsync(ct);
        var overrides = await db.InstructorRevenueShares.AsNoTracking().OrderBy(x => x.CourseId).Select(x => new { x.CourseId, x.InstructorId, x.SharePercent }).ToListAsync(ct);
        return new { defaultInstructorSharePercent = def, overrides };
    }
    /// <summary>TenantAdmin. Upserts the share for one instructor on one course of this academy.</summary>
    public async Task<object> SetCourseShareAsync(Guid courseId, Guid instructorId, double percent, CancellationToken ct)
    {
        if (double.IsNaN(percent) || percent < 0 || percent > 100) throw new ValidationAppException("The percentage must be between 0 and 100.");
        if (!await db.Courses.AnyAsync(c => c.Id == courseId, ct)) throw new NotFoundException("Course not found.");
        if (!await db.Users.AnyAsync(u => u.Id == instructorId && u.TenantId == Tid && u.IsActive, ct)) throw new NotFoundException("Instructor not found.");
        var row = await db.InstructorRevenueShares.FirstOrDefaultAsync(x => x.CourseId == courseId && x.InstructorId == instructorId, ct); double? old = row?.SharePercent;
        if (row is null) db.InstructorRevenueShares.Add(new InstructorRevenueShare { TenantId = Tid, CourseId = courseId, InstructorId = instructorId, SharePercent = percent, CreatedAtUtc = Now });
        else { row.SharePercent = percent; row.UpdatedAtUtc = Now; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(row is null ? AuditLogAction.Create : AuditLogAction.Update, "InstructorRevenueShare", courseId, new { courseId, instructorId, old, percent }, ct);
        return new { courseId, instructorId, sharePercent = percent };
    }

    public async Task<object> MyRevenueAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var f = from ?? DateTime.UtcNow.AddMonths(-1); var t = to ?? DateTime.UtcNow.AddSeconds(1);
        var rows = (await ComputeAsync(f, t, Uid, ct)).Rows;
        var titles = await db.Courses.AsNoTracking().Where(c => rows.Select(r => r.CourseId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var byCourse = rows.GroupBy(r => r.CourseId).Select(g => new { courseId = g.Key, title = titles.GetValueOrDefault(g.Key, ""), sales = g.Count(), net = g.Sum(x => x.Net), earned = Tx.R(g.Sum(x => x.Net * (decimal)x.Share / 100m)), sharePercent = g.First().Share }).ToList();
        var payouts = await db.InstructorPayouts.AsNoTracking().Where(p => p.InstructorId == Uid).OrderByDescending(p => p.CreatedAtUtc).Take(50).Select(p => new { p.Id, p.PeriodStartUtc, p.PeriodEndUtc, p.Amount, p.Currency, Status = p.Status.ToString(), p.PaidAtUtc, p.PaymentReference }).ToListAsync(ct);
        return new { fromUtc = f, toUtc = t, courses = byCourse, totalEarned = byCourse.Sum(c => c.earned), payouts };
    }

    // ---- payouts -------------------------------------------------------------------------------------------------------
    public async Task<object> CalculatePayoutsAsync(PayoutCalcRequest r, CancellationToken ct)
    {
        if (r.PeriodEndUtc <= r.PeriodStartUtc || r.PeriodEndUtc - r.PeriodStartUtc > TimeSpan.FromDays(366)) throw new ValidationAppException("The period must end after it starts and span at most one year.");
        if (r.PeriodEndUtc > DateTime.UtcNow.AddMinutes(1)) throw new ValidationAppException("The period must end in the past.");
        await ComputeAsync(r.PeriodStartUtc.ToUniversalTime(), r.PeriodEndUtc.ToUniversalTime(), null, ct);   // fails fast (409) when no revenue share is configured
        var job = new BackgroundJob { TenantId = Tid, Type = BackgroundJobType.PayoutCalculation, Status = BackgroundJobStatus.Pending, ReferenceType = "PayoutPeriod", RequestedBy = Uid, CreatedAtUtc = Now, ResultJson = System.Text.Json.JsonSerializer.Serialize(new { from = r.PeriodStartUtc, to = r.PeriodEndUtc }) };
        db.BackgroundJobs.Add(job); await db.SaveChangesAsync(ct);
        var hf = queue.Enqueue(job.Id, Tid);
        if (hf is null) await RunPayoutJobAsync(job.Id, ct); else { job.HangfireJobId = hf; await db.SaveChangesAsync(ct); }
        var done = await db.BackgroundJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id, ct);
        return new { jobId = job.Id, status = done.Status.ToString(), queued = hf is not null, result = done.Status == BackgroundJobStatus.Completed ? done.ResultJson : null };
    }

    public async Task RunPayoutJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId, ct);
        if (job.Status is BackgroundJobStatus.Completed or BackgroundJobStatus.Failed) return;
        job.Status = BackgroundJobStatus.Processing; job.StartedAtUtc = Now; await db.SaveChangesAsync(ct);
        try
        {
            var p = System.Text.Json.JsonDocument.Parse(job.ResultJson!).RootElement; var from = p.GetProperty("from").GetDateTime().ToUniversalTime(); var to = p.GetProperty("to").GetDateTime().ToUniversalTime();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockTenantAsync(job.TenantId, ct);   // refunds and payout calculations are serialised, so a refund is either inside this payout's numbers or becomes a debit against it
            var rows = (await ComputeAsync(from, to, null, ct)).Rows.ToList(); var created = 0; var skipped = 0; var settledLate = 0;
            // earnings of sales whose held share was released after an earlier payout already covered them: settled now, once, up to the end of this period
            var late = await db.OrderItemShares.Where(x => x.ReleasedLate && x.SettledPayoutId == null && x.SharePercent != null).ToListAsync(ct);
            var lateByKey = new List<(OrderItemShare Share, List<Row> Rows)>();
            foreach (var l in late)
            {
                // allocated period by period exactly as a normal calculation would have done: the cuts are the edges of this instructor's live payouts, plus the end of this period
                var edges = await db.InstructorPayouts.AsNoTracking().Where(x => x.InstructorId == l.InstructorId && x.Status != InstructorPayoutStatus.Cancelled && x.PeriodEndUtc < to).Select(x => new { x.PeriodStartUtc, x.PeriodEndUtc }).ToListAsync(ct);
                var cuts = edges.SelectMany(x => new[] { x.PeriodStartUtc, x.PeriodEndUtc }).Where(x => x < to).Append(to).Distinct().OrderBy(x => x).ToList();
                var lr = new List<Row>(); var prev = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                foreach (var cut in cuts) { if (cut <= prev) continue; lr.AddRange((await ComputeAsync(prev, cut, l.InstructorId, ct, null, true, l.OrderItemId, true)).Rows.Where(x => x.CourseId == l.CourseId)); prev = cut; }
                lateByKey.Add((l, lr)); rows.AddRange(lr);
            }
            foreach (var g in rows.GroupBy(x => x.InstructorId))
            {
                var amount = Tx.R(g.Sum(x => x.Net * (decimal)x.Share / 100m)); if (amount <= 0) continue;
                // A period that overlaps an earlier live payout is skipped so the same sale is never paid twice.
                var overlap = await db.InstructorPayouts.AnyAsync(x => x.InstructorId == g.Key && x.Status != InstructorPayoutStatus.Cancelled && x.PeriodStartUtc < to && x.PeriodEndUtc > from, ct);
                if (overlap) { skipped++; continue; }
                var currency = await (from c in db.Courses where c.OwnerInstructorId == g.Key select c.Currency).FirstOrDefaultAsync(ct) ?? "EGP";
                // debits from refunds of already-calculated earnings are carried against this payout; the money is never taken back automatically
                var debits = await db.InstructorDebits.Where(x => x.InstructorId == g.Key && x.RemainingAmount > 0).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
                var payout = new InstructorPayout { TenantId = job.TenantId, InstructorId = g.Key, PeriodStartUtc = from, PeriodEndUtc = to, Amount = amount, Currency = currency, Status = InstructorPayoutStatus.Calculated, CreatedAtUtc = Now };
                var left = amount;
                foreach (var d in debits)
                {
                    if (left <= 0) break;
                    var take = Math.Min(d.RemainingAmount, left); d.RemainingAmount -= take; left -= take;
                    db.InstructorDebitApplications.Add(new InstructorDebitApplication { TenantId = job.TenantId, InstructorDebitId = d.Id, InstructorPayoutId = payout.Id, Amount = take });
                }
                payout.Amount = left;
                db.InstructorPayouts.Add(payout);
                // immutable record of what this payout earned per order line: refund debits are computed from it later
                foreach (var pl in g.Where(x => x.Order != Guid.Empty).GroupBy(x => (x.Order, x.Item, x.CourseId)))
                    db.InstructorPayoutLines.Add(new InstructorPayoutLine { TenantId = job.TenantId, InstructorPayoutId = payout.Id, OrderId = pl.Key.Order, OrderItemId = pl.Key.Item, CourseId = pl.Key.CourseId,
                        GrossEarned = Tx.R(pl.Sum(x => x.GrossNet * (decimal)x.Share / 100m)), PaidEarned = Tx.R(pl.Sum(x => x.Net * (decimal)x.Share / 100m)) });
                foreach (var (sh, lr) in lateByKey.Where(x => x.Share.InstructorId == g.Key && x.Rows.Count > 0)) { sh.SettledPayoutId = payout.Id; settledLate++; }
                created++;
            }
            await db.SaveChangesAsync(ct);
            job.Status = BackgroundJobStatus.Completed; job.ProgressPercent = 100; job.CompletedAtUtc = Now; job.ResultJson = System.Text.Json.JsonSerializer.Serialize(new { created, skippedOverlapping = skipped, settledLateReleases = settledLate });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            await db.BackgroundJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BackgroundJobStatus.Failed).SetProperty(j => j.ErrorMessage, "Payout calculation failed.").SetProperty(j => j.CompletedAtUtc, Now), ct);
        }
    }

    public async Task<object> PayoutsAsync(PagedQueryLite q, CancellationToken ct)
    {
        var total = await db.InstructorPayouts.CountAsync(ct);
        var items = await db.InstructorPayouts.AsNoTracking().OrderByDescending(p => p.CreatedAtUtc).Skip(q.Skip).Take(q.Size).Select(p => new { p.Id, p.InstructorId, name = db.Users.Where(u => u.Id == p.InstructorId).Select(u => u.FullName).FirstOrDefault(), p.PeriodStartUtc, p.PeriodEndUtc, p.Amount, p.Currency, Status = p.Status.ToString(), p.PaidAtUtc, p.PaymentReference }).ToListAsync(ct);
        return new { items, page = q.Page, pageSize = q.Size, totalCount = total };
    }

    private async Task LockTenantAsync(Guid tenantId, CancellationToken ct) =>
        await db.Tenants.IgnoreQueryFilters().Where(x => x.Id == tenantId).ExecuteUpdateAsync(x => x.SetProperty(y => y.InstructorSharePercent, y => y.InstructorSharePercent), ct);   // row lock held until the transaction ends

    public sealed record RefundRequest(decimal Amount, string Reference, string? Reason);

    /// <summary>Records a refund as an accounting fact (the refund itself happens outside the system). Instructor earnings are reversed in proportion to the refunded amount: earnings not yet calculated are reduced
    /// by the refunded share, and earnings already inside a calculated payout become a debit carried against the instructor's next payouts. No money is withdrawn.
    /// The order row is locked first, so concurrent requests cannot over-refund; the reference (for example the gateway's refund id) makes a repeat of the same request return the original record.</summary>
    public async Task<object> RecordRefundAsync(Guid orderId, RefundRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Reference) || r.Reference.Trim().Length > 100) throw new ValidationAppException("A refund reference (for example the gateway refund id), up to 100 characters, is required.");
        if (r.Amount <= 0 || r.Amount != Tx.R(r.Amount)) throw new ValidationAppException("The refund amount must be positive with at most two decimals.");
        if (r.Reason is { Length: > 500 }) throw new ValidationAppException("The reason is at most 500 characters.");
        var reference = r.Reference.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTenantAsync(Tid, ct);
        var locked = await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Total, o => o.Total), ct);   // takes the row lock until commit
        if (locked == 0) throw new NotFoundException("Order not found.");
        var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        if (order.Status != OrderStatus.Paid) throw new ConflictAppException("ORDER_NOT_PAID", "Only a paid order can be refunded.");
        var prior = await db.OrderRefunds.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId && x.Reference == reference, ct);
        if (prior is not null)
        {
            if (prior.Amount != r.Amount) throw new ConflictAppException("REFUND_REFERENCE_REUSED", "This refund reference was already recorded with a different amount.");
            return new { refundId = prior.Id, orderId, amount = prior.Amount, duplicate = true, note = "Already recorded; nothing changed." };
        }
        var already = await db.OrderRefunds.Where(x => x.OrderId == orderId).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        if (already + r.Amount > order.Total) throw new ValidationAppException($"The refunds would exceed the order total ({order.Total:0.00}); {already:0.00} is already refunded.");
        var refund = new OrderRefund { TenantId = Tid, OrderId = orderId, Reference = reference, Amount = r.Amount, Reason = r.Reason?.Trim(), RecordedBy = Uid, CreatedAtUtc = Now };
        db.OrderRefunds.Add(refund);
        var ratio = r.Amount / order.Total; var debits = new List<object>();
        var refundIds = await db.OrderRefunds.Where(x => x.OrderId == orderId).Select(x => x.Id).ToListAsync(ct);
        // debits come from the immutable payout lines of live payouts, never from recomputed (mutable) activity
        var lines = await (from l in db.InstructorPayoutLines.AsNoTracking() join p in db.InstructorPayouts.AsNoTracking() on l.InstructorPayoutId equals p.Id
                           where l.OrderId == orderId && p.Status != InstructorPayoutStatus.Cancelled select new { l.InstructorPayoutId, p.InstructorId, p.Currency, l.GrossEarned, l.PaidEarned }).ToListAsync(ct);
        foreach (var g in lines.GroupBy(x => (x.InstructorPayoutId, x.InstructorId, x.Currency)))
        {
            var gross = g.Sum(x => x.GrossEarned); var paid = g.Sum(x => x.PaidEarned);
            // a payout's debits for one order can never exceed what that order earned inside it, however many partial refunds there are
            var taken = await db.InstructorDebits.Where(d => d.SourcePayoutId == g.Key.InstructorPayoutId && refundIds.Contains(d.OrderRefundId)).SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;
            var amount = Math.Min(Tx.R(gross * ratio), paid - taken); if (amount <= 0) continue;
            db.InstructorDebits.Add(new InstructorDebit { TenantId = Tid, InstructorId = g.Key.InstructorId, OrderRefundId = refund.Id, SourcePayoutId = g.Key.InstructorPayoutId, Amount = amount, RemainingAmount = amount, Currency = g.Key.Currency, CreatedAtUtc = Now });
            debits.Add(new { instructorId = g.Key.InstructorId, payoutId = g.Key.InstructorPayoutId, amount });
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Create, "OrderRefund", refund.Id, new { orderId, r.Amount, reference, debits = debits.Count }, ct);
        await tx.CommitAsync(ct);
        return new { refundId = refund.Id, orderId, amount = r.Amount, refundedTotal = already + r.Amount, duplicate = false, debitsCreated = debits, note = "Accounting record only: no money was refunded or withdrawn by the system." };
    }

    public sealed record HeldReleaseRequest(Guid OrderItemId, Guid CourseId, Guid InstructorId, double Percent, int? NonVideoSeconds = null, int? NonVideoLessonCount = null);

    /// <summary>Revenue with no percentage recorded at purchase stays held (user decision, Telegram 12773511, relayed by the main agent) until the TenantAdmin reviews it and sets a percentage explicitly.</summary>
    public async Task<object> HeldAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(366)) throw new ValidationAppException("The period must end after it starts and span at most one year.");
        var c = await ComputeAsync(fromUtc, toUtc, null, ct);
        var items = (c.HeldLines ?? []).GroupBy(h => (h.OrderItemId, h.CourseId, h.InstructorId)).Select(g => new { orderItemId = g.Key.OrderItemId, courseId = g.Key.CourseId, instructorId = g.Key.InstructorId, amount = g.Sum(x => x.Net), kind = g.First().Kind }).ToList();
        return new { fromUtc, toUtc, items, totalHeld = c.HeldNoFrozenShare };
    }

    public async Task<object> ReleaseHeldAsync(HeldReleaseRequest r, CancellationToken ct)
    {
        if (double.IsNaN(r.Percent) || r.Percent < 0 || r.Percent > 100) throw new ValidationAppException("The percentage must be between 0 and 100.");
        var line = await (from i in db.OrderItems.AsNoTracking() join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id where i.Id == r.OrderItemId && o.Status == OrderStatus.Paid select new { i.ItemType, Paid = o.PaidAtUtc }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Paid order line not found.");
        var row = await db.OrderItemShares.FirstOrDefaultAsync(x => x.OrderItemId == r.OrderItemId && x.CourseId == r.CourseId && x.InstructorId == r.InstructorId, ct);
        var hasAny = await db.OrderItemShares.AnyAsync(x => x.OrderItemId == r.OrderItemId && x.CourseId == r.CourseId, ct);
        if (row is not null && row.SharePercent is not null) throw new ConflictAppException("SHARE_ALREADY_RECORDED", "A percentage was recorded for this line at purchase; it cannot be changed.");
        if (row is null)
        {
            if (hasAny) throw new ConflictAppException("SHARE_NOT_HELD", "This instructor is not a held earner for this line.");
            var owner = await db.Courses.AsNoTracking().Where(c => c.Id == r.CourseId).Select(c => (Guid?)c.OwnerInstructorId).FirstOrDefaultAsync(ct);
            if (line.ItemType != OrderItemItemType.Plan || owner != r.InstructorId) throw new ConflictAppException("SHARE_NOT_HELD", "Only a course added to a subscription plan after purchase, and its owner, can be released this way.");
        }
        // earnings must not already sit inside a calculated payout period, or the released amount could never be paid
        // when a payout of this instructor already covers the sale, the release is settled in the next payout calculated; the payouts already made are never rewritten
        var late = await db.InstructorPayouts.AnyAsync(p => p.InstructorId == r.InstructorId && p.Status != InstructorPayoutStatus.Cancelled && p.PeriodEndUtc > line.Paid, ct);
        var old = row?.SharePercent;
        // A course added to a plan after purchase has no frozen non-video allowance. The TenantAdmin may set one explicitly while reviewing; nothing is taken from today's settings.
        if ((r.NonVideoSeconds is null) != (r.NonVideoLessonCount is null) || r.NonVideoSeconds is < 0 || r.NonVideoLessonCount is < 0) throw new ValidationAppException("Give both nonVideoSeconds and nonVideoLessonCount, each zero or more, or neither.");
        if (r.NonVideoSeconds is not null)
        {
            if (line.ItemType != OrderItemItemType.Plan) throw new ValidationAppException("A non-video allowance only applies to a subscription plan line.");
            if (await db.OrderItemAllocations.AnyAsync(x => x.OrderItemId == r.OrderItemId && x.CourseId == r.CourseId, ct)) throw new ConflictAppException("ALLOWANCE_ALREADY_RECORDED", "An allowance was recorded for this course at purchase; it cannot be changed.");
            db.OrderItemAllocations.Add(new OrderItemAllocation { TenantId = Tid, OrderItemId = r.OrderItemId, CourseId = r.CourseId, ListPrice = 0, NonVideoSeconds = r.NonVideoSeconds, NonVideoLessonCount = r.NonVideoLessonCount });
        }
        if (row is null) db.OrderItemShares.Add(new OrderItemShare { TenantId = Tid, OrderItemId = r.OrderItemId, CourseId = r.CourseId, InstructorId = r.InstructorId, SharePercent = r.Percent, ReleasedLate = late });
        else { row.SharePercent = r.Percent; row.ReleasedLate = late; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Approve, "HeldRevenueShare", r.OrderItemId, new { r.CourseId, r.InstructorId, old, r.Percent }, ct);
        return new { r.OrderItemId, r.CourseId, r.InstructorId, sharePercent = r.Percent, settlement = late ? "next payout calculated" : "normal payout of its period" };
    }

    public async Task<object> DebitsAsync(CancellationToken ct)
    {
        var items = await db.InstructorDebits.AsNoTracking().OrderByDescending(d => d.CreatedAtUtc).Take(200).Select(d => new { d.Id, d.InstructorId, d.OrderRefundId, d.SourcePayoutId, d.Amount, d.RemainingAmount, d.Currency, d.CreatedAtUtc }).ToListAsync(ct);
        return new { items, outstanding = items.Sum(x => x.RemainingAmount) };
    }

    private async Task RestoreDebitsAsync(Guid payoutId, CancellationToken ct)
    {
        var apps = await db.InstructorDebitApplications.Where(x => x.InstructorPayoutId == payoutId).ToListAsync(ct);
        foreach (var a in apps) { var d = await db.InstructorDebits.FirstAsync(x => x.Id == a.InstructorDebitId, ct); d.RemainingAmount += a.Amount; }
        db.InstructorDebitApplications.RemoveRange(apps);
        foreach (var sh in await db.OrderItemShares.Where(x => x.SettledPayoutId == payoutId).ToListAsync(ct)) sh.SettledPayoutId = null;   // a cancelled payout settles nothing: the released revenue waits for the next one
        // debits created because THIS payout was refunded after the fact disappear with it: its recalculation already nets the refund
        var own = await db.InstructorDebits.Where(x => x.SourcePayoutId == payoutId).ToListAsync(ct); db.InstructorDebits.RemoveRange(own);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdatePayoutAsync(Guid id, PayoutUpdateRequest r, CancellationToken ct)
    {
        var to = Enum.TryParse<InstructorPayoutStatus>(r.Status, true, out var s) && s != InstructorPayoutStatus.Calculated && Enum.IsDefined(s) ? s : throw new ValidationAppException("Status must be Approved, Paid or Cancelled.");
        var p = await db.InstructorPayouts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Payout not found.");
        var from = to switch { InstructorPayoutStatus.Approved => new[] { InstructorPayoutStatus.Calculated }, InstructorPayoutStatus.Paid => [InstructorPayoutStatus.Approved], _ => [InstructorPayoutStatus.Calculated, InstructorPayoutStatus.Approved] };
        if (to == InstructorPayoutStatus.Paid && (string.IsNullOrWhiteSpace(r.PaymentReference) || r.PaymentReference.Length > 200)) throw new ValidationAppException("A payment reference up to 200 characters is required to mark a payout paid.");
        if (to == InstructorPayoutStatus.Cancelled && await db.InstructorDebits.AnyAsync(d => d.SourcePayoutId == id && d.RemainingAmount < d.Amount, ct))
            throw new ConflictAppException("DEBIT_ALREADY_APPLIED", "A refund debit created from this payout has already been carried against a later payout, so this payout cannot be cancelled.");
        var admin = Uid; var reference = r.PaymentReference?.Trim();
        var n = await db.InstructorPayouts.Where(x => x.Id == id && from.Contains(x.Status)).ExecuteUpdateAsync(x => x
            .SetProperty(y => y.Status, to).SetProperty(y => y.ApprovedBy, admin).SetProperty(y => y.PaidAtUtc, to == InstructorPayoutStatus.Paid ? Now : p.PaidAtUtc).SetProperty(y => y.PaymentReference, to == InstructorPayoutStatus.Paid ? reference : p.PaymentReference), ct);
        if (n == 0) throw new ConflictAppException("INVALID_TRANSITION", $"A {p.Status} payout cannot become {to}.");
        if (to == InstructorPayoutStatus.Cancelled) await RestoreDebitsAsync(id, ct);
        await audit.WriteAsync(to == InstructorPayoutStatus.Cancelled ? AuditLogAction.Reject : AuditLogAction.Approve, "InstructorPayout", id, new { status = to.ToString(), reference, p.Amount }, ct);
        await notifier.SendAsync(p.TenantId, [p.InstructorId], "PayoutUpdated", "Payout " + to.ToString().ToLowerInvariant(), $"{p.Amount:0.00} {p.Currency}", ct);
    }
}

/// <summary>Hangfire entry point for the Phase 4 jobs. Runs without a user, so the tenant comes from the job row's tenant.</summary>
public sealed class BackgroundJobRunner(ApplicationDbContext db, ITenantContext tenant, BatchGenerator vouchers, RevenueService revenue, WebhookService hooks, SmartLMS.Infrastructure.Services.Ai.AiJobService aiJobs)
{
    [Hangfire.AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(Guid jobId, Guid tenantId)
    {
        tenant.Set(tenantId, false);
        var job = await db.BackgroundJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
        if (job is null || job.Status is BackgroundJobStatus.Completed or BackgroundJobStatus.Failed) return;
        switch (job.Type)
        {
            case BackgroundJobType.VoucherGeneration: await vouchers.RunAsync(jobId, CancellationToken.None); break;
            case BackgroundJobType.PayoutCalculation: await revenue.RunPayoutJobAsync(jobId, CancellationToken.None); break;
            case BackgroundJobType.AiIngest: await aiJobs.RunIngestAsync(jobId, CancellationToken.None); break;
            case BackgroundJobType.PlagiarismBatch: await aiJobs.RunPlagiarismAsync(jobId, CancellationToken.None); break;
            case BackgroundJobType.WebhookDelivery:
                await hooks.DeliverAsync(job.ReferenceId!.Value, CancellationToken.None);
                await db.BackgroundJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BackgroundJobStatus.Completed).SetProperty(j => j.CompletedAtUtc, DateTime.UtcNow));
                break;
        }
    }

    /// <summary>Recurring: retries webhook deliveries whose backoff time has passed, for every tenant that has some.</summary>
    [Hangfire.DisableConcurrentExecution(300)]
    public async Task RetryWebhooksAsync()
    {
        var due = await db.WebhookDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAtUtc <= DateTime.UtcNow).Select(d => d.TenantId).Distinct().Take(50).ToListAsync();
        foreach (var t in due) { tenant.Set(t, false); await hooks.DeliverDueAsync(CancellationToken.None); }
    }
}
