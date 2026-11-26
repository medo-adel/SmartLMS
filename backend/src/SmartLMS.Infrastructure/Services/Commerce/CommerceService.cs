using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.Infrastructure.Services.Commerce;

public sealed record CartItemRequest(string ItemType, Guid ItemId);
public sealed record CouponCodeRequest(string Code);
public sealed record CheckoutRequest(string PaymentMethod, string? CouponCode = null, string? Gateway = null, string? VoucherCode = null, string? ReferralCode = null);
public sealed record BundleRequest(string Name, string? Description, decimal Price, string? Currency, List<Guid> CourseIds);
public sealed record PlanRequest(string Name, string? Description, decimal Price, string? Currency, string BillingPeriod, string Scope, List<Guid>? CourseIds);
public sealed record SubscribeRequest(Guid PlanId, string PaymentMethod, string? Gateway = null);
public sealed record CouponRequest(string Code, string DiscountType, decimal Value, int? MaxRedemptions, int? MaxPerUser, decimal? MinOrderAmount, DateTime? ExpiresAtUtc, Guid? CourseId, bool? IsActive);
public sealed record TopUpGatewayRequest(decimal Amount, string Gateway);

/// <summary>
/// Cart, coupons, bundles, plans, orders, checkout and order fulfilment. Prices always come from the database at checkout, never from the client.
/// An order becomes Paid through one conditional UPDATE, so a repeated webhook, double click or approval cannot grant access or pay commission twice.
/// </summary>
public sealed class CommerceService(ApplicationDbContext db, ICurrentUser me, WalletLedger ledger, GatewayRegistry gateways, Notifier notifier, WebhookService hooks, TransactionalMailer mail)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> CouponLocks = new();

    private sealed record Line(Guid CartItemId, CartItemItemType Type, Guid RefId, Guid? CourseId, string Description, decimal Price, string Currency);

    private static T ParseEnum<T>(string? v, string what) where T : struct, Enum =>
        Enum.TryParse<T>(v, true, out var r) && Enum.IsDefined(r) ? r : throw new ValidationAppException($"Unknown {what}: {v}");

    // ---- pricing -------------------------------------------------------------------------------------------------------
    private async Task<Line> PriceAsync(CartItemItemType type, Guid id, Guid cartItemId, CancellationToken ct)
    {
        switch (type)
        {
            case CartItemItemType.Course:
                var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id && x.Status == CourseStatus.Published, ct) ?? throw new NotFoundException("Course not found.");
                if (c.PricingModel != CoursePricingModel.OneTime || c.Price <= 0) throw new AppException(400, "NOT_PURCHASABLE", "This course is not sold as a one-time purchase.");
                return new Line(cartItemId, type, id, c.Id, c.Title, Tx.R(c.Price), c.Currency);
            case CartItemItemType.Lesson:
                var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, ct) ?? throw new NotFoundException("Lesson not found.");
                var lc = await db.Courses.FirstOrDefaultAsync(x => x.Id == l.CourseId && x.Status == CourseStatus.Published, ct) ?? throw new NotFoundException("Lesson not found.");
                if (l.PayPerLessonPrice is not > 0) throw new AppException(400, "NOT_PURCHASABLE", "This lesson is not sold on its own.");
                return new Line(cartItemId, type, id, lc.Id, $"{lc.Title}: {l.Title}", Tx.R(l.PayPerLessonPrice.Value), lc.Currency);
            case CartItemItemType.Bundle:
                var b = await db.Bundles.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct) ?? throw new NotFoundException("Bundle not found.");
                return new Line(cartItemId, type, id, null, b.Name, Tx.R(b.Price), b.Currency);
            case CartItemItemType.Plan:
                var p = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct) ?? throw new NotFoundException("Plan not found.");
                return new Line(cartItemId, type, id, null, p.Name, Tx.R(p.Price), p.Currency);
            case CartItemItemType.Path:
                var lp = await db.LearningPaths.FirstOrDefaultAsync(x => x.Id == id && x.Status == LearningPathStatus.Published, ct) ?? throw new NotFoundException("Learning path not found.");
                if (lp.Price <= 0) throw new AppException(400, "NOT_PURCHASABLE", "This learning path is free.");
                return new Line(cartItemId, type, id, null, lp.Title, Tx.R(lp.Price), lp.Currency);
            default: throw new ValidationAppException("Unknown item type.");
        }
    }

    private static bool LiveEnrollment(Enrollment e) => e.Status is EnrollmentStatus.Active or EnrollmentStatus.Completed && (e.ExpiresAtUtc is null || e.ExpiresAtUtc > Now);

    private async Task EnsureNotOwnedAsync(Line l, Guid uid, CancellationToken ct)
    {
        bool owned;
        switch (l.Type)
        {
            case CartItemItemType.Course:
                var e = await db.Enrollments.FirstOrDefaultAsync(x => x.UserId == uid && x.CourseId == l.RefId, ct);
                owned = e is not null && LiveEnrollment(e); break;
            case CartItemItemType.Lesson:
                var e2 = await db.Enrollments.FirstOrDefaultAsync(x => x.UserId == uid && x.CourseId == l.CourseId, ct);
                owned = (e2 is not null && LiveEnrollment(e2)) || await db.LessonAccessGrants.AnyAsync(g => g.UserId == uid && g.LessonId == l.RefId && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > Now), ct); break;
            case CartItemItemType.Bundle:
                var courseIds = await db.BundleCourses.Where(x => x.BundleId == l.RefId).Select(x => x.CourseId).ToListAsync(ct);
                var live = await db.Enrollments.Where(x => x.UserId == uid && courseIds.Contains(x.CourseId) && (x.Status == EnrollmentStatus.Active || x.Status == EnrollmentStatus.Completed) && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > Now)).CountAsync(ct);
                owned = courseIds.Count > 0 && live >= courseIds.Count; break;
            case CartItemItemType.Plan:
                owned = await db.UserSubscriptions.AnyAsync(s => s.UserId == uid && s.PlanId == l.RefId && s.Status == UserSubscriptionStatus.Active && s.CurrentPeriodEndUtc > Now, ct); break;
            default:
                owned = await db.LearningPathEnrollments.AnyAsync(x => x.UserId == uid && x.LearningPathId == l.RefId && x.Status != LearningPathEnrollmentStatus.Revoked, ct); break;
        }
        if (owned) throw new ConflictAppException("ALREADY_OWNED", $"You already have access to {l.Description}.");
    }

    // ---- cart ----------------------------------------------------------------------------------------------------------
    public async Task<object> CartAsync(CancellationToken ct)
    {
        var items = await db.CartItems.Where(c => c.UserId == Uid).OrderBy(c => c.CreatedAtUtc).ToListAsync(ct);
        var rows = new List<object>(); decimal subtotal = 0; string? currency = null;
        foreach (var it in items)
        {
            var (type, refId) = Ref(it);
            try
            {
                var l = await PriceAsync(type, refId, it.Id, ct);
                subtotal += l.Price; currency ??= l.Currency;
                rows.Add(new { it.Id, ItemType = type.ToString(), ItemId = refId, l.Description, l.Price, l.Currency, available = true });
            }
            catch (AppException) { rows.Add(new { it.Id, ItemType = type.ToString(), ItemId = refId, Description = "No longer available", Price = 0m, Currency = currency ?? "EGP", available = false }); }
        }
        return new { items = rows, subtotal = Tx.R(subtotal), currency = currency ?? "EGP" };
    }

    private static (CartItemItemType, Guid) Ref(CartItem c) => c.ItemType switch
    {
        CartItemItemType.Course => (c.ItemType, c.CourseId!.Value),
        CartItemItemType.Lesson => (c.ItemType, c.LessonId!.Value),
        CartItemItemType.Bundle => (c.ItemType, c.BundleId!.Value),
        CartItemItemType.Plan => (c.ItemType, c.PlanId!.Value),
        _ => (c.ItemType, c.LearningPathId!.Value),
    };

    public async Task<Guid> AddToCartAsync(CartItemRequest r, CancellationToken ct)
    {
        var type = ParseEnum<CartItemItemType>(r.ItemType, "item type");
        var line = await PriceAsync(type, r.ItemId, Guid.Empty, ct);
        await EnsureNotOwnedAsync(line, Uid, ct);
        var existing = (await db.CartItems.Where(c => c.UserId == Uid && c.ItemType == type).ToListAsync(ct)).FirstOrDefault(c => Ref(c).Item2 == r.ItemId);
        if (existing is not null) return existing.Id;
        if (await db.CartItems.CountAsync(c => c.UserId == Uid, ct) >= 50) throw new ValidationAppException("The cart can hold at most 50 items.");
        var item = new CartItem { TenantId = Tid, UserId = Uid, ItemType = type, CreatedAtUtc = Now };
        switch (type)
        {
            case CartItemItemType.Course: item.CourseId = r.ItemId; break;
            case CartItemItemType.Lesson: item.LessonId = r.ItemId; break;
            case CartItemItemType.Bundle: item.BundleId = r.ItemId; break;
            case CartItemItemType.Plan: item.PlanId = r.ItemId; break;
            default: item.LearningPathId = r.ItemId; break;
        }
        db.CartItems.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(item).State = EntityState.Detached; var again = (await db.CartItems.Where(c => c.UserId == Uid && c.ItemType == type).ToListAsync(ct)).FirstOrDefault(c => Ref(c).Item2 == r.ItemId); if (again is not null) return again.Id; throw; }
        return item.Id;
    }

    public async Task RemoveFromCartAsync(Guid id, CancellationToken ct)
    {
        var c = await db.CartItems.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Uid, ct) ?? throw new NotFoundException("Cart item not found.");
        db.CartItems.Remove(c); await db.SaveChangesAsync(ct);
    }

    // ---- wishlist ------------------------------------------------------------------------------------------------------
    public async Task<object> WishlistAsync(CancellationToken ct)
    {
        var items = await db.WishlistItems.Where(w => w.UserId == Uid).OrderByDescending(w => w.CreatedAtUtc).ToListAsync(ct);
        var ids = items.Select(i => i.CourseId).ToList();
        var courses = await db.Courses.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var rows = new List<object>();
        foreach (var i in items)
        {
            if (!courses.TryGetValue(i.CourseId, out var c)) continue;
            rows.Add(new { id = i.Id, courseId = c.Id, c.Title, c.Slug, c.ShortDescription, c.ThumbnailFileId, pricingModel = c.PricingModel.ToString(), c.Price, c.Currency, createdAtUtc = i.CreatedAtUtc });
        }
        return new { items = rows };
    }

    public async Task<Guid> AddToWishlistAsync(Guid courseId, CancellationToken ct)
    {
        var exists = await db.Courses.AnyAsync(c => c.Id == courseId && c.Status == CourseStatus.Published, ct);
        if (!exists) throw new NotFoundException("Course not found.");
        var existing = await db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == Uid && w.CourseId == courseId, ct);
        if (existing is not null) return existing.Id;
        var item = new WishlistItem { TenantId = Tid, UserId = Uid, CourseId = courseId, CreatedAtUtc = Now };
        db.WishlistItems.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(item).State = EntityState.Detached; var again = await db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == Uid && w.CourseId == courseId, ct); if (again is not null) return again.Id; throw; }
        return item.Id;
    }

    public async Task RemoveFromWishlistAsync(Guid courseId, CancellationToken ct)
    {
        var w = await db.WishlistItems.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct) ?? throw new NotFoundException("Wishlist item not found.");
        db.WishlistItems.Remove(w); await db.SaveChangesAsync(ct);
    }

    private async Task<List<Line>> CartLinesAsync(CancellationToken ct)
    {
        var items = await db.CartItems.Where(c => c.UserId == Uid).OrderBy(c => c.CreatedAtUtc).ToListAsync(ct);
        if (items.Count == 0) throw new ValidationAppException("The cart is empty.");
        var lines = new List<Line>();
        foreach (var it in items) { var (t, id) = Ref(it); var l = await PriceAsync(t, id, it.Id, ct); await EnsureNotOwnedAsync(l, Uid, ct); lines.Add(l); }
        return lines;
    }

    // ---- coupons -------------------------------------------------------------------------------------------------------
    /// <summary>
    /// Cross-instance guard. Inside the checkout transaction, on SQL Server, a transaction-scoped application lock per coupon serialises concurrent checkouts across app instances;
    /// then the limits are counted again over the other orders' committed redemptions. On other providers (tests) the in-process lock above is the only guard.
    /// UNVERIFIED on a live SQL Server here.
    /// </summary>
    private async Task RecheckCouponLimitsAsync(Coupon c, Guid orderId, Guid uid, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @r < 0 THROW 51000, 'coupon lock timeout', 1;", new object[] { "coupon:" + c.Id }, ct);
        var cutoff = Now.AddHours(-2);
        var others = db.CouponRedemptions.Where(r => r.CouponId == c.Id && r.OrderId != orderId && db.Orders.Any(o => o.Id == r.OrderId && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.AwaitingManualReview || (o.Status == OrderStatus.Pending && o.CreatedAtUtc > cutoff))));
        if (c.MaxRedemptions is { } max && await others.CountAsync(ct) >= max) throw new AppException(400, "COUPON_LIMIT_REACHED", "This coupon has been used the maximum number of times.");
        if (await others.CountAsync(r => r.UserId == uid, ct) >= c.MaxPerUser) throw new AppException(400, "COUPON_LIMIT_REACHED", "You have already used this coupon the maximum number of times.");
    }

    private async Task<(Coupon C, decimal Discount)> ValidateCouponAsync(string code, IReadOnlyList<Line> lines, CancellationToken ct)
    {
        code = code.Trim().ToUpperInvariant();
        var c = await db.Coupons.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (c is null || !c.IsActive) throw new AppException(400, "COUPON_INVALID", "This coupon code is not valid.");
        if (c.ExpiresAtUtc is { } e && e <= Now) throw new AppException(400, "COUPON_EXPIRED", "This coupon has expired.");
        var subtotal = lines.Sum(l => l.Price);
        var eligible = c.CourseId is null ? subtotal : lines.Where(l => l.CourseId == c.CourseId).Sum(l => l.Price);
        if (eligible <= 0) throw new AppException(400, "COUPON_NOT_APPLICABLE", "This coupon does not apply to the items in your cart.");
        if (c.MinOrderAmount is { } min && subtotal < min) throw new AppException(400, "COUPON_MIN_ORDER", $"This coupon needs an order of at least {min:0.00}.");
        var cutoff = Now.AddHours(-2);
        var live = db.CouponRedemptions.Where(r => r.CouponId == c.Id && db.Orders.Any(o => o.Id == r.OrderId && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.AwaitingManualReview || (o.Status == OrderStatus.Pending && o.CreatedAtUtc > cutoff))));
        if (c.MaxRedemptions is { } max && await live.CountAsync(ct) >= max) throw new AppException(400, "COUPON_LIMIT_REACHED", "This coupon has been used the maximum number of times.");
        if (await live.CountAsync(r => r.UserId == Uid, ct) >= c.MaxPerUser) throw new AppException(400, "COUPON_LIMIT_REACHED", "You have already used this coupon the maximum number of times.");
        var discount = c.DiscountType == CouponDiscountType.Percentage ? Tx.R(eligible * c.Value / 100m) : Math.Min(Tx.R(c.Value), eligible);
        return (c, discount);
    }

    public async Task<object> ApplyCouponAsync(CouponCodeRequest r, CancellationToken ct)
    {
        var lines = await CartLinesAsync(ct);
        var (c, d) = await ValidateCouponAsync(r.Code, lines, ct);
        var sub = lines.Sum(l => l.Price);
        return new { couponId = c.Id, code = c.Code, discount = d, subtotal = Tx.R(sub), total = Tx.R(sub - d), currency = lines[0].Currency, note = "Send this code again as couponCode at checkout. The server re-checks it then." };
    }

    private static string NormalizeCode(string? code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        if (c.Length is < 3 or > 40 || !c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) throw new ValidationAppException("The code must be 3 to 40 letters, digits, '-' or '_'.");
        return c;
    }
    private void ValidateCoupon(CouponRequest r, CouponDiscountType type)
    {
        if (type == CouponDiscountType.Percentage && r.Value is <= 0 or > 100) throw new ValidationAppException("A percentage coupon must be between 0 and 100.");
        if (r.Value <= 0) throw new ValidationAppException("The coupon value must be positive.");
        if (r.MaxRedemptions is < 1 || r.MaxPerUser is < 1) throw new ValidationAppException("Limits must be at least 1.");
        if (r.MinOrderAmount is < 0) throw new ValidationAppException("The minimum order cannot be negative.");
    }
    public async Task<object> CouponsAsync(CancellationToken ct) =>
        await db.Coupons.AsNoTracking().OrderByDescending(c => c.CreatedAtUtc).Select(c => new { c.Id, c.Code, DiscountType = c.DiscountType.ToString(), c.Value, c.MaxRedemptions, c.MaxPerUser, c.MinOrderAmount, c.ExpiresAtUtc, c.CourseId, c.IsActive, redemptions = db.CouponRedemptions.Count(x => x.CouponId == c.Id) }).Take(500).ToListAsync(ct);
    public async Task<Guid> CreateCouponAsync(CouponRequest r, CancellationToken ct)
    {
        var type = ParseEnum<CouponDiscountType>(r.DiscountType, "discount type"); ValidateCoupon(r, type);
        if (r.CourseId is { } cid && !await db.Courses.AnyAsync(c => c.Id == cid, ct)) throw new NotFoundException("Course not found.");
        var c = new Coupon { TenantId = Tid, Code = NormalizeCode(r.Code), DiscountType = type, Value = Tx.R(r.Value), MaxRedemptions = r.MaxRedemptions, MaxPerUser = r.MaxPerUser ?? 1, MinOrderAmount = r.MinOrderAmount, ExpiresAtUtc = r.ExpiresAtUtc, CourseId = r.CourseId, IsActive = r.IsActive ?? true, CreatedAtUtc = Now };
        db.Coupons.Add(c);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw new ConflictAppException("COUPON_EXISTS", "A coupon with this code already exists."); }
        return c.Id;
    }
    public async Task UpdateCouponAsync(Guid id, CouponRequest r, CancellationToken ct)
    {
        var c = await db.Coupons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Coupon not found.");
        var type = ParseEnum<CouponDiscountType>(r.DiscountType, "discount type"); ValidateCoupon(r, type);
        if (r.CourseId is { } cid && !await db.Courses.AnyAsync(x => x.Id == cid, ct)) throw new NotFoundException("Course not found.");
        // The code never changes: it is what past redemptions and invoices point at.
        c.DiscountType = type; c.Value = Tx.R(r.Value); c.MaxRedemptions = r.MaxRedemptions; c.MaxPerUser = r.MaxPerUser ?? c.MaxPerUser; c.MinOrderAmount = r.MinOrderAmount; c.ExpiresAtUtc = r.ExpiresAtUtc; c.CourseId = r.CourseId; if (r.IsActive is { } a) c.IsActive = a;
        await db.SaveChangesAsync(ct);
    }

    // ---- bundles and plans ---------------------------------------------------------------------------------------------
    public async Task<object> BundlesAsync(CancellationToken ct)
    {
        var bs = await db.Bundles.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Take(200).ToListAsync(ct);
        var ids = bs.Select(b => b.Id).ToList();
        var links = await (from bc in db.BundleCourses where ids.Contains(bc.BundleId) join c in db.Courses on bc.CourseId equals c.Id select new { bc.BundleId, c.Id, c.Title }).ToListAsync(ct);
        return bs.Select(b => new { b.Id, b.Name, b.Description, b.Price, b.Currency, courses = links.Where(l => l.BundleId == b.Id).Select(l => new { l.Id, l.Title }) });
    }
    public async Task<Guid> CreateBundleAsync(BundleRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200) throw new ValidationAppException("A name up to 200 characters is required.");
        if (r.Price <= 0) throw new ValidationAppException("The price must be positive.");
        var ids = (r.CourseIds ?? []).Distinct().ToList(); if (ids.Count < 2) throw new ValidationAppException("A bundle needs at least two courses.");
        var courses = await db.Courses.Where(c => ids.Contains(c.Id) && c.Status == CourseStatus.Published).Select(c => c.Currency).ToListAsync(ct);
        if (courses.Count != ids.Count) throw new NotFoundException("Every bundled course must exist and be published.");
        var cur = (r.Currency ?? "EGP").ToUpperInvariant(); if (cur.Length != 3) throw new ValidationAppException("The currency must be a 3-letter code.");
        var b = new Bundle { TenantId = Tid, Name = r.Name.Trim(), Description = r.Description, Price = Tx.R(r.Price), Currency = cur, CreatedAtUtc = Now };
        db.Bundles.Add(b); foreach (var id in ids) db.BundleCourses.Add(new BundleCourse { TenantId = Tid, BundleId = b.Id, CourseId = id, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct); return b.Id;
    }
    public async Task<object> PlansAsync(CancellationToken ct) =>
        await db.SubscriptionPlans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Price).Select(p => new { p.Id, p.Name, p.Description, p.Price, p.Currency, BillingPeriod = p.BillingPeriod.ToString(), Scope = p.Scope.ToString() }).Take(200).ToListAsync(ct);
    public async Task<Guid> CreatePlanAsync(PlanRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200) throw new ValidationAppException("A name up to 200 characters is required.");
        if (r.Price <= 0) throw new ValidationAppException("The price must be positive.");
        var period = ParseEnum<SubscriptionPlanBillingPeriod>(r.BillingPeriod, "billing period"); var scope = ParseEnum<SubscriptionPlanScope>(r.Scope, "scope");
        var ids = (r.CourseIds ?? []).Distinct().ToList();
        if (scope == SubscriptionPlanScope.SelectedCourses)
        {
            if (ids.Count == 0) throw new ValidationAppException("A plan for selected courses needs at least one course.");
            if (await db.Courses.CountAsync(c => ids.Contains(c.Id) && c.Status == CourseStatus.Published, ct) != ids.Count) throw new NotFoundException("Every course must exist and be published.");
        }
        else if (ids.Count > 0) throw new ValidationAppException("An all-courses plan takes no course list.");
        var p = new SubscriptionPlan { TenantId = Tid, Name = r.Name.Trim(), Description = r.Description, Price = Tx.R(r.Price), Currency = (r.Currency ?? "EGP").ToUpperInvariant(), BillingPeriod = period, Scope = scope, CreatedAtUtc = Now };
        db.SubscriptionPlans.Add(p); foreach (var id in ids) db.SubscriptionPlanCourses.Add(new SubscriptionPlanCourse { TenantId = Tid, PlanId = p.Id, CourseId = id, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct); return p.Id;
    }
    private static DateTime AddPeriod(DateTime from, SubscriptionPlanBillingPeriod p) => p switch { SubscriptionPlanBillingPeriod.Monthly => from.AddMonths(1), SubscriptionPlanBillingPeriod.Quarterly => from.AddMonths(3), _ => from.AddYears(1) };

    public async Task<object> SubscribeAsync(SubscribeRequest r, CancellationToken ct)
    {
        var line = await PriceAsync(CartItemItemType.Plan, r.PlanId, Guid.Empty, ct); await EnsureNotOwnedAsync(line, Uid, ct);
        return await CreateOrderAsync([line], new CheckoutRequest(r.PaymentMethod, Gateway: r.Gateway), false, ct);
    }
    /// <summary>GET /subscriptions/mine: the caller's subscriptions with plan display fields. Powers the cancel UI; ids stay server-side.</summary>
    public async Task<object> MySubscriptionsAsync(CancellationToken ct) =>
        await (from us in db.UserSubscriptions.AsNoTracking() join pl in db.SubscriptionPlans.AsNoTracking() on us.PlanId equals pl.Id
               where us.UserId == Uid
               orderby us.StartedAtUtc descending
               select new { id = us.Id, planName = pl.Name, price = pl.Price, currency = pl.Currency, billingPeriod = pl.BillingPeriod.ToString(), status = us.Status.ToString(), startedAtUtc = us.StartedAtUtc, currentPeriodEndUtc = us.CurrentPeriodEndUtc, cancelledAtUtc = us.CancelledAtUtc }).ToListAsync(ct);

    public async Task CancelSubscriptionAsync(Guid id, CancellationToken ct)
    {
        var s = await db.UserSubscriptions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Uid, ct) ?? throw new NotFoundException("Subscription not found.");
        if (s.Status != UserSubscriptionStatus.Active) throw new ConflictAppException("NOT_ACTIVE", "This subscription is not active.");
        s.Status = UserSubscriptionStatus.Cancelled; s.CancelledAtUtc = Now;   // access continues until CurrentPeriodEndUtc
        await db.SaveChangesAsync(ct);
    }

    /// <summary>POST /lessons/{id}/unlock: pay a pay-per-lesson price from the wallet. Safe to repeat: a second call never charges again.</summary>
    public async Task<object> UnlockLessonAsync(Guid lessonId, CancellationToken ct)
    {
        var line = await PriceAsync(CartItemItemType.Lesson, lessonId, Guid.Empty, ct);
        try { await EnsureNotOwnedAsync(line, Uid, ct); } catch (ConflictAppException) { return new { unlocked = true, charged = false, lessonId }; }
        var order = await CreateOrderAsync([line], new CheckoutRequest("wallet"), false, ct);
        return new { unlocked = true, charged = true, lessonId, order };
    }

    // ---- checkout ------------------------------------------------------------------------------------------------------
    public async Task<object> CheckoutAsync(CheckoutRequest r, CancellationToken ct) => await CreateOrderAsync(await CartLinesAsync(ct), r, true, ct);

    private async Task<object> CreateOrderAsync(List<Line> lines, CheckoutRequest r, bool fromCart, CancellationToken ct)
    {
        var uid = Uid; var tid = Tid;
        if (lines.Select(l => l.Currency).Distinct().Count() > 1) throw new ValidationAppException("All items in one order must use the same currency.");
        var currency = lines[0].Currency; var subtotal = Tx.R(lines.Sum(l => l.Price));
        var method = (r.PaymentMethod ?? "").Trim().ToLowerInvariant();
        if (method is not ("gateway" or "wallet" or "voucher" or "manual")) throw new ValidationAppException("PaymentMethod must be gateway, wallet, voucher or manual.");
        IPaymentGateway? gw = null;

        Coupon? coupon = null; decimal discount = 0;
        SemaphoreSlim? gate = null;
        if (!string.IsNullOrWhiteSpace(r.CouponCode))
        {
            var cc = await db.Coupons.AsNoTracking().Where(c => c.Code == r.CouponCode.Trim().ToUpper()).Select(c => c.Id).FirstOrDefaultAsync(ct);
            if (cc != Guid.Empty) { gate = CouponLocks.GetOrAdd(cc, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(ct); }
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(r.CouponCode)) (coupon, discount) = await ValidateCouponAsync(r.CouponCode, lines, ct);
            var total = Tx.R(Math.Max(0, subtotal - discount));
            var free = total == 0;
            if (!free && method == "gateway")
            {
                gw = gateways.Find(r.Gateway ?? "") ?? throw new ValidationAppException("Gateway must be Paymob, Fawry or Stripe.");
                if (!gw.Enabled) throw new AppException(400, "GATEWAY_NOT_CONFIGURED", "This payment gateway is not enabled for this installation.");
            }
            Voucher? voucher = null; VoucherBatche? batch = null;
            if (!free && method == "voucher")
            {
                if (lines.Count != 1 || lines[0].Type != CartItemItemType.Course || string.IsNullOrWhiteSpace(r.VoucherCode)) throw new ValidationAppException("A voucher pays for exactly one course. Send voucherCode with a single course in the cart.");
                (voucher, batch) = await VoucherService.FindUsableAsync(db, uid, r.VoucherCode, ct);
                if (batch.VoucherType != VoucherBatcheVoucherType.CourseAccess || batch.CourseId != lines[0].CourseId) throw new AppException(400, "VOUCHER_NOT_APPLICABLE", "This voucher is not for this course.");
            }

            var orderMethod = free ? OrderPaymentMethod.Free : method switch { "gateway" => OrderPaymentMethod.Gateway, "wallet" => OrderPaymentMethod.Wallet, "voucher" => OrderPaymentMethod.Voucher, _ => OrderPaymentMethod.Manual };
            if (orderMethod == OrderPaymentMethod.Voucher) { discount = subtotal; total = 0; }

            var result = await Tx.RunAsync(db, async () =>
            {
                var o = new Order { TenantId = tid, UserId = uid, Status = orderMethod == OrderPaymentMethod.Manual ? OrderStatus.AwaitingManualReview : OrderStatus.Pending, Subtotal = subtotal, DiscountTotal = discount, Total = total, Currency = currency, CouponId = coupon?.Id, PaymentMethod = orderMethod, CreatedAtUtc = Now };
                db.Orders.Add(o);
                foreach (var l in lines) db.OrderItems.Add(new OrderItem
                {
                    TenantId = tid, OrderId = o.Id, ItemType = Enum.Parse<OrderItemItemType>(l.Type.ToString()), CourseId = l.Type == CartItemItemType.Course ? l.RefId : null, LessonId = l.Type == CartItemItemType.Lesson ? l.RefId : null,
                    BundleId = l.Type == CartItemItemType.Bundle ? l.RefId : null, PlanId = l.Type == CartItemItemType.Plan ? l.RefId : null, LearningPathId = l.Type == CartItemItemType.Path ? l.RefId : null,
                    Description = l.Description.Length > 300 ? l.Description[..300] : l.Description, UnitPrice = l.Price, Quantity = 1, LineTotal = l.Price, CreatedAtUtc = Now,
                });
                // Fixed at purchase: for every course a line covers, its list price, its non-video learning allowance (plans) and who earns what percentage. Later edits never move earned money.
                foreach (var oi in db.ChangeTracker.Entries<OrderItem>().Select(e => e.Entity).Where(x => x.OrderId == o.Id).ToList()) await SnapshotAsync(oi, tid, ct);
                if (coupon is not null) db.CouponRedemptions.Add(new CouponRedemption { TenantId = tid, CouponId = coupon.Id, UserId = uid, OrderId = o.Id, RedeemedAtUtc = Now, CreatedAtUtc = Now });
                await AttachReferralAsync(o, uid, tid, r.ReferralCode, ct);
                if (fromCart) await db.CartItems.Where(c => c.UserId == uid).ExecuteDeleteAsync(ct);
                await db.SaveChangesAsync(ct);
                if (coupon is not null) await RecheckCouponLimitsAsync(coupon, o.Id, uid, ct);

                switch (orderMethod)
                {
                    case OrderPaymentMethod.Gateway:
                        var p = new Payment { TenantId = tid, OrderId = o.Id, Method = gw!.Method, Amount = total, Currency = currency, Status = PaymentStatus.Pending, CreatedAtUtc = Now, IdempotencyKey = $"order:{o.Id}:{gw.Method}" };
                        db.Payments.Add(p); var co = await gw.CreateCheckoutAsync(p.Id, total, currency, await BillingAsync(uid, ct), ct); p.GatewayReference = co.Reference; await db.SaveChangesAsync(ct);
                        return (object)new { orderId = o.Id, status = o.Status.ToString(), total, currency, paymentId = p.Id, checkoutUrl = co.Url, reference = co.Reference };
                    case OrderPaymentMethod.Manual:
                        return new { orderId = o.Id, status = o.Status.ToString(), total, currency, next = "Transfer the amount, then POST /manual-payments with this orderId and the receipt image." };
                    case OrderPaymentMethod.Wallet:
                        await ledger.ApplyAsync(tid, uid, WalletTransactionType.Purchase, -total, $"order:{o.Id}", "Order", o.Id, "Order payment", ct);
                        db.Payments.Add(new Payment { TenantId = tid, OrderId = o.Id, Method = PaymentMethod.Wallet, Amount = total, Currency = currency, Status = PaymentStatus.Succeeded, CreatedAtUtc = Now, ConfirmedAtUtc = Now, IdempotencyKey = $"order:{o.Id}:wallet" });
                        await db.SaveChangesAsync(ct); await FulfillOrderAsync(o.Id, EnrollmentSource.Wallet, ct);
                        return new { orderId = o.Id, status = OrderStatus.Paid.ToString(), total, currency };
                    case OrderPaymentMethod.Voucher:
                        if (!await VoucherService.ClaimAsync(db, voucher!.Id, uid, ct)) throw new AppException(400, "VOUCHER_INVALID", "This voucher code is not valid.");
                        db.Payments.Add(new Payment { TenantId = tid, OrderId = o.Id, Method = PaymentMethod.Voucher, Amount = 0, Currency = currency, Status = PaymentStatus.Succeeded, CreatedAtUtc = Now, ConfirmedAtUtc = Now, IdempotencyKey = $"order:{o.Id}:voucher" });
                        await db.SaveChangesAsync(ct); await FulfillOrderAsync(o.Id, EnrollmentSource.Voucher, ct);
                        return new { orderId = o.Id, status = OrderStatus.Paid.ToString(), total, currency };
                    default:   // Free (a coupon took the total to zero)
                        await FulfillOrderAsync(o.Id, EnrollmentSource.Purchase, ct);
                        return new { orderId = o.Id, status = OrderStatus.Paid.ToString(), total, currency };
                }
            });
            return result;
        }
        finally { gate?.Release(); }
    }

    private async Task AttachReferralAsync(Order o, Guid uid, Guid tid, string? code, CancellationToken ct)
    {
        code = code?.Trim().ToUpperInvariant(); if (string.IsNullOrEmpty(code)) return;
        var ap = await db.AffiliateProfiles.FirstOrDefaultAsync(a => a.ReferralCode == code && a.IsActive, ct);
        if (ap is null || ap.UserId == uid) return;
        o.ReferralCode = code;
        if (!await db.Referrals.AnyAsync(x => x.ReferredUserId == uid, ct))
            db.Referrals.Add(new Referral { TenantId = tid, AffiliateUserId = ap.UserId, ReferredUserId = uid, ReferralCodeUsed = code, CreatedAtUtc = Now });
    }

    // ---- fulfilment ----------------------------------------------------------------------------------------------------
    /// <summary>Marks the order Paid and grants everything in it. Returns false when it was already paid (nothing is granted twice).</summary>
    public async Task<bool> FulfillOrderAsync(Guid orderId, EnrollmentSource source, CancellationToken ct)
    {
        var won = await Tx.RunAsync(db, async () =>
        {
            var flipped = await db.Orders.Where(o => o.Id == orderId && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.AwaitingManualReview))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid).SetProperty(o => o.PaidAtUtc, Now), ct);
            if (flipped == 0) return false;
            var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
            var items = await db.OrderItems.Where(i => i.OrderId == orderId).ToListAsync(ct);
            foreach (var it in items) await GrantAsync(order, it, source, ct);
            await IssueInvoiceAsync(order, ct);
            await PayCommissionAsync(order, items, ct);
            return true;
        });
        if (!won) return false;
        var o2 = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        await notifier.SendAsync(o2.TenantId, [o2.UserId], "OrderPaid", "Payment received", $"Your order of {o2.Total:0.00} {o2.Currency} is complete.", ct);
        await mail.OrderReceiptAsync(o2.UserId, o2.Id, ct); // best-effort, never throws
        await hooks.EmitAsync(o2.TenantId, "order.paid", new { orderId = o2.Id, userId = o2.UserId, total = o2.Total, currency = o2.Currency }, ct);
        return true;
    }

    public async Task GrantCourseAsync(Guid tid, Guid uid, Guid courseId, EnrollmentSource source, Guid? orderItemId, DateTime? forcedExpiry, CancellationToken ct)
    {
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, ct); if (course is null) return;
        var expires = forcedExpiry ?? (course.AccessDays is { } d ? Now.AddDays(d) : null);
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.UserId == uid && x.CourseId == courseId, ct);
        if (e is null)
        {
            db.Enrollments.Add(new Enrollment { TenantId = tid, UserId = uid, CourseId = courseId, Source = source, Status = EnrollmentStatus.Active, EnrolledAtUtc = Now, ExpiresAtUtc = expires, OrderItemId = orderItemId });
            await db.SaveChangesAsync(ct);
            await mail.EnrollmentAsync(uid, courseId, ct); // best-effort, never throws
        }
        else if (e.Status == EnrollmentStatus.Expired || (e.Status == EnrollmentStatus.Active && e.ExpiresAtUtc is { } x && x <= Now))
        {
            e.Status = EnrollmentStatus.Active; e.ExpiresAtUtc = expires; e.Source = source; await db.SaveChangesAsync(ct);
        }
        // A Revoked enrollment stays revoked: an administrator did that on purpose. The order still shows as Paid for them to resolve.
    }

    private async Task GrantAsync(Order o, OrderItem it, EnrollmentSource source, CancellationToken ct)
    {
        var tid = o.TenantId; var uid = o.UserId;
        switch (it.ItemType)
        {
            case OrderItemItemType.Course: await GrantCourseAsync(tid, uid, it.CourseId!.Value, source, it.Id, null, ct); break;
            case OrderItemItemType.Lesson:
                var lid = it.LessonId!.Value;
                if (!await db.LessonAccessGrants.AnyAsync(g => g.UserId == uid && g.LessonId == lid, ct))
                {
                    db.LessonAccessGrants.Add(new LessonAccessGrant { TenantId = tid, UserId = uid, LessonId = lid, Source = o.PaymentMethod == OrderPaymentMethod.Wallet ? LessonAccessGrantSource.Wallet : LessonAccessGrantSource.PayPerLesson, UnlockedAtUtc = Now, OrderItemId = it.Id, CreatedAtUtc = Now });
                    await db.SaveChangesAsync(ct);
                }
                break;
            case OrderItemItemType.Bundle:
                foreach (var cid in await db.BundleCourses.Where(b => b.BundleId == it.BundleId).Select(b => b.CourseId).ToListAsync(ct)) await GrantCourseAsync(tid, uid, cid, source, it.Id, null, ct);
                break;
            case OrderItemItemType.Plan:
                var plan = await db.SubscriptionPlans.AsNoTracking().FirstAsync(p => p.Id == it.PlanId, ct);
                var sub = await db.UserSubscriptions.Where(s => s.UserId == uid && s.PlanId == plan.Id && s.CurrentPeriodEndUtc > Now && (s.Status == UserSubscriptionStatus.Active || s.Status == UserSubscriptionStatus.Cancelled)).FirstOrDefaultAsync(ct);
                var start = sub?.CurrentPeriodEndUtc ?? Now;
                var end = AddPeriod(start, plan.BillingPeriod);
                if (sub is null) db.UserSubscriptions.Add(new UserSubscription { TenantId = tid, UserId = uid, PlanId = plan.Id, Status = UserSubscriptionStatus.Active, StartedAtUtc = Now, CurrentPeriodEndUtc = end, CreatedAtUtc = Now });
                else { sub.CurrentPeriodEndUtc = end; sub.Status = UserSubscriptionStatus.Active; sub.CancelledAtUtc = null; }
                await db.SaveChangesAsync(ct);
                if (plan.Scope == SubscriptionPlanScope.SelectedCourses)
                    foreach (var cid in await db.SubscriptionPlanCourses.Where(p => p.PlanId == plan.Id).Select(p => p.CourseId).ToListAsync(ct)) await GrantCourseAsync(tid, uid, cid, EnrollmentSource.Subscription, it.Id, end, ct);
                break;
            case OrderItemItemType.Path:
                var pid = it.LearningPathId!.Value;
                if (!await db.LearningPathEnrollments.AnyAsync(x => x.UserId == uid && x.LearningPathId == pid, ct)) { db.LearningPathEnrollments.Add(new LearningPathEnrollment { TenantId = tid, UserId = uid, LearningPathId = pid, CreatedAtUtc = Now }); await db.SaveChangesAsync(ct); }
                foreach (var cid in await db.LearningPathCourses.Where(x => x.LearningPathId == pid).Select(x => x.CourseId).ToListAsync(ct)) await GrantCourseAsync(tid, uid, cid, EnrollmentSource.Path, it.Id, null, ct);
                break;
            case OrderItemItemType.WalletTopUp:
                await ledger.ApplyAsync(tid, uid, o.PaymentMethod == OrderPaymentMethod.Manual ? WalletTransactionType.TopUpManual : WalletTransactionType.TopUpGateway, it.LineTotal, $"topup:{o.Id}", "Order", o.Id, "Wallet top-up", ct);
                break;
        }
    }

    private async Task IssueInvoiceAsync(Order o, CancellationToken ct)
    {
        if (await db.Invoices.AnyAsync(i => i.OrderId == o.Id, ct)) return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var n = await db.Invoices.IgnoreQueryFilters().CountAsync(i => i.TenantId == o.TenantId, ct) + 1 + attempt;
            var inv = new Invoice { TenantId = o.TenantId, OrderId = o.Id, InvoiceNumber = $"INV-{Now:yyyyMM}-{n:D6}", IssuedAtUtc = Now, CreatedAtUtc = Now };
            db.Invoices.Add(inv);
            try { await db.SaveChangesAsync(ct); return; }
            catch (DbUpdateException) { db.Entry(inv).State = EntityState.Detached; }
        }
        throw new ConflictAppException("INVOICE_NUMBER", "Could not allocate an invoice number.");
    }

    private async Task PayCommissionAsync(Order o, List<OrderItem> items, CancellationToken ct)
    {
        if (o.Total <= 0 || items.All(i => i.ItemType == OrderItemItemType.WalletTopUp)) return;
        var rf = await db.Referrals.AsNoTracking().FirstOrDefaultAsync(r => r.ReferredUserId == o.UserId, ct); if (rf is null) return;
        var ap = await db.AffiliateProfiles.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == rf.AffiliateUserId && a.IsActive, ct); if (ap is null) return;
        var amount = Tx.R(o.Total * (decimal)ap.CommissionPercent / 100m); if (amount <= 0) return;
        if (await db.AffiliateCommissions.AnyAsync(c => c.AffiliateUserId == ap.UserId && c.OrderId == o.Id, ct)) return;
        await ledger.ApplyAsync(o.TenantId, ap.UserId, WalletTransactionType.AffiliateCommission, amount, $"aff:{o.Id}", "Order", o.Id, "Referral commission", ct);
        var tx = await db.WalletTransactions.AsNoTracking().Where(t => t.IdempotencyKey == $"aff:{o.Id}").Select(t => t.Id).FirstOrDefaultAsync(ct);
        db.AffiliateCommissions.Add(new AffiliateCommission { TenantId = o.TenantId, AffiliateUserId = ap.UserId, OrderId = o.Id, Amount = amount, Status = AffiliateCommissionStatus.Credited, WalletTransactionId = tx == Guid.Empty ? null : tx, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }

    // ---- orders --------------------------------------------------------------------------------------------------------
    private bool Staff => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    public async Task<object> OrdersAsync(PagedQueryLite q, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.UserId == Uid);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.CreatedAtUtc).Skip(q.Skip).Take(q.Size).Select(o => new { o.Id, Status = o.Status.ToString(), o.Subtotal, o.DiscountTotal, o.Total, o.Currency, PaymentMethod = o.PaymentMethod.ToString(), o.CreatedAtUtc, o.PaidAtUtc }).ToListAsync(ct);
        return new { items, page = q.Page, pageSize = q.Size, totalCount = total };
    }
    private async Task<Order> OwnOrderAsync(Guid id, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Order not found.");
        if (o.UserId != Uid && !Staff) throw new NotFoundException("Order not found.");
        return o;
    }
    public async Task<object> OrderAsync(Guid id, CancellationToken ct)
    {
        var o = await OwnOrderAsync(id, ct);
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == id).Select(i => new { i.Id, ItemType = i.ItemType.ToString(), i.Description, i.UnitPrice, i.Quantity, i.LineTotal }).ToListAsync(ct);
        var pays = await db.Payments.AsNoTracking().Where(p => p.OrderId == id).Select(p => new { p.Id, Method = p.Method.ToString(), Status = p.Status.ToString(), p.Amount, p.Currency, p.CreatedAtUtc, p.ConfirmedAtUtc, p.FailureReason }).ToListAsync(ct);
        var inv = await db.Invoices.AsNoTracking().Where(i => i.OrderId == id).Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct);
        return new { o.Id, o.UserId, Status = o.Status.ToString(), o.Subtotal, o.DiscountTotal, o.Total, o.Currency, PaymentMethod = o.PaymentMethod.ToString(), o.CreatedAtUtc, o.PaidAtUtc, items, payments = pays, invoiceNumber = inv };
    }
    /// <summary>Writes the immutable allocation inputs of one order line (see OrderItemAllocations and OrderItemShares).</summary>
    private async Task SnapshotAsync(OrderItem oi, Guid tid, CancellationToken ct)
    {
        List<Guid> courseIds;
        switch (oi.ItemType)
        {
            case OrderItemItemType.Course: courseIds = oi.CourseId is Guid c ? [c] : []; break;
            case OrderItemItemType.Lesson: courseIds = await db.Lessons.Where(l => l.Id == oi.LessonId).Select(l => l.CourseId).ToListAsync(ct); break;
            case OrderItemItemType.Bundle: courseIds = await db.BundleCourses.Where(b => b.BundleId == oi.BundleId).Select(b => b.CourseId).ToListAsync(ct); break;
            case OrderItemItemType.Path: courseIds = await db.LearningPathCourses.Where(b => b.LearningPathId == oi.LearningPathId).Select(b => b.CourseId).ToListAsync(ct); break;
            case OrderItemItemType.Plan:
                var plan = await db.SubscriptionPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == oi.PlanId && p.TenantId == tid, ct);
                courseIds = plan is null ? [] : plan.Scope == SubscriptionPlanScope.SelectedCourses ? await db.SubscriptionPlanCourses.Where(x => x.PlanId == plan.Id).Select(x => x.CourseId).ToListAsync(ct) : await db.Courses.Where(x => x.Status == CourseStatus.Published).Select(x => x.Id).ToListAsync(ct);
                break;
            default: return;
        }
        if (courseIds.Count == 0) return;
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).Select(c => new { c.Id, c.Price, c.OwnerInstructorId }).ToListAsync(ct);
        var weights = await db.CourseLearningWeights.Where(w => courseIds.Contains(w.CourseId)).ToDictionaryAsync(w => w.CourseId, w => w.NonVideoCourseSeconds, ct);
        var nonVideo = await db.Lessons.Where(l => courseIds.Contains(l.CourseId) && l.LessonType != LessonType.Video && l.LessonType != LessonType.Audio && !l.IsFreePreview && l.IsPublished).GroupBy(l => l.CourseId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var overrides = await db.InstructorRevenueShares.Where(x => courseIds.Contains(x.CourseId)).Select(x => new { x.CourseId, x.InstructorId, x.SharePercent }).ToListAsync(ct);
        var tenantShare = await db.Tenants.Where(x => x.Id == tid).Select(x => x.InstructorSharePercent).FirstOrDefaultAsync(ct);
        foreach (var c in courses)
        {
            db.OrderItemAllocations.Add(new OrderItemAllocation { TenantId = tid, OrderItemId = oi.Id, CourseId = c.Id, ListPrice = c.Price, NonVideoSeconds = oi.ItemType == OrderItemItemType.Plan ? weights.GetValueOrDefault(c.Id) : null, NonVideoLessonCount = oi.ItemType == OrderItemItemType.Plan ? nonVideo.GetValueOrDefault(c.Id) : null, CreatedAtUtc = Now });
            var mine = overrides.Where(x => x.CourseId == c.Id).ToList();
            if (mine.Count == 0) db.OrderItemShares.Add(new OrderItemShare { TenantId = tid, OrderItemId = oi.Id, CourseId = c.Id, InstructorId = c.OwnerInstructorId, SharePercent = tenantShare, CreatedAtUtc = Now });
            else foreach (var m in mine) db.OrderItemShares.Add(new OrderItemShare { TenantId = tid, OrderItemId = oi.Id, CourseId = c.Id, InstructorId = m.InstructorId, SharePercent = m.SharePercent, CreatedAtUtc = Now });
        }
    }

    public async Task<byte[]> InvoiceAsync(Guid id, CancellationToken ct)
    {
        var o = await OwnOrderAsync(id, ct);
        var inv = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.OrderId == id, ct) ?? throw new AppException(409, "NOT_PAID", "An invoice exists only after the order is paid.");
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == id).ToListAsync(ct);
        var name = await db.Users.Where(u => u.Id == o.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Customer";
        return InvoicePdf.Build(inv.InvoiceNumber, inv.IssuedAtUtc, name, o.Currency, items.Select(i => new InvoiceLine(i.Description, i.Quantity, i.UnitPrice, i.LineTotal)).ToList(), o.Subtotal, o.DiscountTotal, o.Total, o.Status.ToString());
    }

    // ---- gateway callbacks and top-ups ---------------------------------------------------------------------------------
    /// <summary>
    /// Handles a provider callback. Paymob: POST {"obj":{...}} with ?hmac= (HMAC-SHA512 over the documented field order). Other gateways: normalized body
    /// {"reference","status":"succeeded|failed","amount","currency"} with X-Signature (HMAC-SHA256 hex over the raw body).
    /// </summary>
    public async Task<object> GatewayWebhookAsync(string gateway, byte[] body, string? signature, CancellationToken ct)
    {
        var gw = gateways.Find(gateway) ?? throw new NotFoundException("Unknown gateway.");
        var cb = gw.Enabled ? gw.ReadCallback(body, signature) : null;
        if (cb is null) throw new UnauthorizedAppException("INVALID_SIGNATURE", "The callback signature is not valid.");
        var b = new { Reference = cb.Reference, Amount = (decimal?)cb.Amount, Currency = cb.Currency };
        var pay = await db.Payments.FirstOrDefaultAsync(p => p.GatewayReference == b.Reference && p.Method == gw.Method, ct) ?? throw new NotFoundException("Payment not found.");
        var ok = cb.Succeeded;
        if (pay.Status != PaymentStatus.Pending) return new { processed = false, reason = "already " + pay.Status.ToString().ToLowerInvariant() };
        if (ok && (Tx.R(b.Amount!.Value) != pay.Amount || !string.Equals(b.Currency, pay.Currency, StringComparison.OrdinalIgnoreCase)))
        {
            await db.Payments.Where(p => p.Id == pay.Id && p.Status == PaymentStatus.Pending).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed).SetProperty(p => p.FailureReason, "AMOUNT_MISMATCH"), ct);
            return new { processed = false, reason = "amount or currency mismatch, payment marked failed" };
        }
        if (!ok)
        {
            var f = await db.Payments.Where(p => p.Id == pay.Id && p.Status == PaymentStatus.Pending).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed).SetProperty(p => p.FailureReason, "GATEWAY_DECLINED"), ct);
            if (f == 1) await db.Orders.Where(o => o.Id == pay.OrderId && o.Status == OrderStatus.Pending).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Failed), ct);
            return new { processed = f == 1, reason = "failed" };
        }
        return await Tx.RunAsync(db, async () =>
        {
            var flipped = await db.Payments.Where(p => p.Id == pay.Id && p.Status == PaymentStatus.Pending).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Succeeded).SetProperty(p => p.ConfirmedAtUtc, Now), ct);
            if (flipped == 0) return (object)new { processed = false, reason = "already processed" };
            await FulfillOrderAsync(pay.OrderId, EnrollmentSource.Purchase, ct);
            return new { processed = true, reason = "paid" };
        });
    }

    private async Task<BillingInfo> BillingAsync(Guid uid, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == uid).Select(x => new { x.FullName, x.Email, x.PhoneNumber }).FirstOrDefaultAsync(ct);
        return new BillingInfo(u?.FullName ?? "Student", u?.Email, u?.PhoneNumber);
    }

    public async Task<object> TopUpGatewayAsync(TopUpGatewayRequest r, CancellationToken ct)
    {
        var amount = Tx.R(r.Amount); if (amount < 1 || amount > 50000) throw new ValidationAppException("The top-up must be between 1 and 50,000.");
        var gw = gateways.Find(r.Gateway ?? "") ?? throw new ValidationAppException("Gateway must be Paymob, Fawry or Stripe.");
        if (!gw.Enabled) throw new AppException(400, "GATEWAY_NOT_CONFIGURED", "This payment gateway is not enabled for this installation.");
        var tid = Tid; var uid = Uid; var currency = (await ledger.EnsureAsync(tid, uid, ct)).Currency;
        return await Tx.RunAsync(db, async () =>
        {
            var o = new Order { TenantId = tid, UserId = uid, Status = OrderStatus.Pending, Subtotal = amount, Total = amount, Currency = currency, PaymentMethod = OrderPaymentMethod.Gateway, CreatedAtUtc = Now };
            db.Orders.Add(o);
            db.OrderItems.Add(new OrderItem { TenantId = tid, OrderId = o.Id, ItemType = OrderItemItemType.WalletTopUp, Description = "Wallet top-up", UnitPrice = amount, Quantity = 1, LineTotal = amount, CreatedAtUtc = Now });
            var p = new Payment { TenantId = tid, OrderId = o.Id, Method = gw.Method, Amount = amount, Currency = currency, Status = PaymentStatus.Pending, CreatedAtUtc = Now, IdempotencyKey = $"topup:{o.Id}:{gw.Method}" };
            db.Payments.Add(p); var co = await gw.CreateCheckoutAsync(p.Id, amount, currency, await BillingAsync(uid, ct), ct); p.GatewayReference = co.Reference;
            await db.SaveChangesAsync(ct);
            return (object)new { orderId = o.Id, paymentId = p.Id, checkoutUrl = co.Url, reference = co.Reference };
        });
    }
}

public sealed record PagedQueryLite(int Page = 1, int PageSize = 20)
{
    public int Size => Math.Clamp(PageSize, 1, 100);
    public int Skip => (Math.Max(1, Page) - 1) * Size;
}
