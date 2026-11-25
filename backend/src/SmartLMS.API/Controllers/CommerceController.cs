using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartLMS.Application.Common;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Services.Commerce;

namespace SmartLMS.API.Controllers;

[Route("api/v1")]
public sealed class CommerceController(CommerceService c) : ApiControllerBase
{
    private const string St = RoleNames.Student;
    private const string Ta = RoleNames.TenantAdmin;

    [HttpGet("cart"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Cart(CancellationToken ct) => Success(await c.CartAsync(ct));
    [HttpPost("cart/items"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Add(CartItemRequest r, CancellationToken ct) => Success((object)new { id = await c.AddToCartAsync(r, ct) });
    [HttpDelete("cart/items/{id:guid}"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object?>>> Remove(Guid id, CancellationToken ct) { await c.RemoveFromCartAsync(id, ct); return Done("Removed."); }
    [HttpPost("cart/coupon"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Coupon(CouponCodeRequest r, CancellationToken ct) => Success(await c.ApplyCouponAsync(r, ct));
    [HttpPost("orders/checkout"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Checkout(CheckoutRequest r, CancellationToken ct) => Success(await c.CheckoutAsync(r, ct));
    [HttpGet("orders"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Orders([FromQuery] PagedQueryLite q, CancellationToken ct) => Success(await c.OrdersAsync(q, ct));
    [HttpGet("orders/{id:guid}"), Authorize(Roles = $"{St},{Ta}")] public async Task<ActionResult<ApiResponse<object>>> Order(Guid id, CancellationToken ct) => Success(await c.OrderAsync(id, ct));
    [HttpGet("orders/{id:guid}/invoice"), Authorize(Roles = $"{St},{Ta}")] public async Task<IActionResult> Invoice(Guid id, CancellationToken ct) => File(await c.InvoiceAsync(id, ct), "application/pdf", $"invoice-{id:N}.pdf");

    [HttpPost("payments/gateways/{gateway}/webhook"), AllowAnonymous, DisableRequestSizeLimit]
    public async Task<ActionResult<ApiResponse<object>>> Webhook(string gateway, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        if (ms.Length > 256 * 1024) return StatusCode(413, ApiResponse<object>.Fail("Payload too large.", code: "PAYLOAD_TOO_LARGE"));
        return Success(await c.GatewayWebhookAsync(gateway, ms.ToArray(), Request.Headers["X-Signature"].FirstOrDefault() ?? Request.Query["hmac"].FirstOrDefault(), ct));
    }

    [HttpGet("bundles"), AllowAnonymous] public async Task<ActionResult<ApiResponse<object>>> Bundles(CancellationToken ct) => Success(await c.BundlesAsync(ct));
    [HttpPost("bundles"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> CreateBundle(BundleRequest r, CancellationToken ct) => Success((object)new { id = await c.CreateBundleAsync(r, ct) });
    [HttpGet("subscription-plans"), AllowAnonymous] public async Task<ActionResult<ApiResponse<object>>> Plans(CancellationToken ct) => Success(await c.PlansAsync(ct));
    [HttpPost("subscription-plans"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> CreatePlan(PlanRequest r, CancellationToken ct) => Success((object)new { id = await c.CreatePlanAsync(r, ct) });
    [HttpGet("subscriptions/mine"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> MySubscriptions(CancellationToken ct) => Success(await c.MySubscriptionsAsync(ct));
    [HttpPost("subscriptions"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Subscribe(SubscribeRequest r, CancellationToken ct) => Success(await c.SubscribeAsync(r, ct));
    [HttpPost("subscriptions/{id:guid}/cancel"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object?>>> Cancel(Guid id, CancellationToken ct) { await c.CancelSubscriptionAsync(id, ct); return Done("Cancelled."); }
    [HttpPost("lessons/{id:guid}/unlock"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Unlock(Guid id, CancellationToken ct) => Success(await c.UnlockLessonAsync(id, ct));

    [HttpGet("coupons"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Coupons(CancellationToken ct) => Success(await c.CouponsAsync(ct));
    [HttpPost("coupons"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> CreateCoupon(CouponRequest r, CancellationToken ct) => Success((object)new { id = await c.CreateCouponAsync(r, ct) });
    [HttpPut("coupons/{id:guid}"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> UpdateCoupon(Guid id, CouponRequest r, CancellationToken ct) { await c.UpdateCouponAsync(id, r, ct); return Done("Updated."); }
}

[Route("api/v1")]
public sealed class WalletController(WalletService w, CommerceService c) : ApiControllerBase
{
    private const string St = RoleNames.Student;
    private const string Ta = RoleNames.TenantAdmin;

    [HttpGet("wallet"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Wallet(CancellationToken ct) => Success(await w.WalletAsync(ct));
    [HttpGet("wallet/transactions"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Tx([FromQuery] PagedQueryLite q, CancellationToken ct) => Success(await w.TransactionsAsync(q, ct));
    [HttpPost("wallet/topup/gateway"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> TopUp(TopUpGatewayRequest r, CancellationToken ct) => Success(await c.TopUpGatewayAsync(r, ct));
    [HttpPost("wallet/topup/voucher"), Authorize(Roles = St), EnableRateLimiting("voucher")] public async Task<ActionResult<ApiResponse<object>>> TopUpVoucher(VoucherCodeRequest r, CancellationToken ct) => Success(await w.TopUpVoucherAsync(r, ct));
    [HttpPost("vouchers/redeem"), Authorize(Roles = St), EnableRateLimiting("voucher")] public async Task<ActionResult<ApiResponse<object>>> Redeem(VoucherCodeRequest r, CancellationToken ct) => Success(await w.RedeemCourseVoucherAsync(r, ct));

    [HttpPost("voucher-batches"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> CreateBatch(VoucherBatchRequest r, CancellationToken ct) => Success(await w.CreateBatchAsync(r, ct));
    [HttpGet("voucher-batches"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Batches([FromQuery] PagedQueryLite q, CancellationToken ct) => Success(await w.BatchesAsync(q, ct));
    [HttpGet("voucher-batches/{id:guid}/export"), Authorize(Roles = Ta)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct) { var (s, n) = await w.ExportAsync(id, ct); return File(s, "text/csv", n); }
    [HttpPost("voucher-batches/{id:guid}/void"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Void(Guid id, CancellationToken ct) => Success(await w.VoidBatchAsync(id, ct));

    [HttpPost("manual-payments"), Authorize(Roles = St), Consumes("multipart/form-data"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<object>>> Submit([FromForm] ManualPaymentRequest r, IFormFile? receipt, CancellationToken ct)
    {
        if (receipt is null || receipt.Length == 0) throw new SmartLMS.Domain.Exceptions.ValidationAppException("A receipt file is required.");
        await using var s = receipt.OpenReadStream();
        return Success((object)new { id = await w.SubmitManualAsync(r, s, receipt.FileName, ct) });
    }
    [HttpGet("manual-payments/mine"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Mine(CancellationToken ct) => Success(await w.MineManualAsync(ct));
    [HttpGet("manual-payments"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Queue([FromQuery] string? status, [FromQuery] PagedQueryLite q, CancellationToken ct) => Success(await w.ReviewQueueAsync(status, q, ct));
    /// <summary>Extra route (not in the contract): lets the reviewing admin or the owner view the receipt.</summary>
    [HttpGet("manual-payments/{id:guid}/receipt"), Authorize(Roles = $"{St},{Ta}")]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct) { var (s, t, n) = await w.ReceiptAsync(id, ct); return File(s, t, n); }
    [HttpPost("manual-payments/{id:guid}/approve"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> Approve(Guid id, CancellationToken ct) { await w.ApproveManualAsync(id, ct); return Done("Approved."); }
    [HttpPost("manual-payments/{id:guid}/reject"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> Reject(Guid id, RejectPaymentRequest r, CancellationToken ct) { await w.RejectManualAsync(id, r, ct); return Done("Rejected."); }
}

[Route("api/v1")]
public sealed class EnterpriseController(RevenueService rv, AdminService ad, WebhookService wh) : ApiControllerBase
{
    private const string Ta = RoleNames.TenantAdmin;

    [HttpGet("affiliate/me"), Authorize(Roles = $"{RoleNames.Student},{RoleNames.Instructor}")] public async Task<ActionResult<ApiResponse<object>>> Aff(CancellationToken ct) => Success(await rv.AffiliateMeAsync(ct));
    [HttpPost("affiliate/profiles"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> CreateAff(AffiliateProfileRequest r, CancellationToken ct) => Success(await rv.CreateAffiliateAsync(r, ct));
    public sealed record InstructorShareRequest(double? Percent);
    public sealed record CourseShareRequest(Guid InstructorId, double Percent);
    // Extra routes (not in the contract): the academy's instructor-share settings, managed by its TenantAdmin only.
    [HttpGet("instructor-revenue/shares"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> GetShares(CancellationToken ct) => Success(await rv.GetSharesAsync(ct));
    [HttpPut("instructor-revenue/default"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> SetDefault(InstructorShareRequest r, CancellationToken ct) => Success(await rv.SetDefaultShareAsync(r.Percent, ct));
    [HttpPut("courses/{courseId:guid}/instructor-share"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> SetCourseShare(Guid courseId, CourseShareRequest r, CancellationToken ct) => Success(await rv.SetCourseShareAsync(courseId, r.InstructorId, r.Percent, ct));
    // Extra routes (not in the contract): subscription allocation controls and the review statement, TenantAdmin only.
    [HttpGet("instructor-revenue/subscription-controls"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> GetControls(CancellationToken ct) => Success(await rv.GetSubscriptionControlsAsync(ct));
    [HttpPut("instructor-revenue/subscription-controls"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> SetControls(RevenueService.SubscriptionControlsRequest r, CancellationToken ct) => Success(await rv.SetSubscriptionControlsAsync(r, ct));
    [HttpGet("courses/{courseId:guid}/learning-weight"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> GetWeight(Guid courseId, CancellationToken ct) => Success(await rv.GetLearningWeightAsync(courseId, ct));
    [HttpPut("courses/{courseId:guid}/learning-weight"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> SetWeight(Guid courseId, RevenueService.LearningWeightRequest r, CancellationToken ct) => Success(await rv.SetLearningWeightAsync(courseId, r, ct));
    [HttpPost("orders/{orderId:guid}/refunds"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Refund(Guid orderId, RevenueService.RefundRequest r, CancellationToken ct) => Success(await rv.RecordRefundAsync(orderId, r, ct));
    [HttpGet("instructor-revenue/held"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Held([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct) => Success(await rv.HeldAsync(fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(), ct));
    [HttpPut("instructor-revenue/held"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> ReleaseHeld(RevenueService.HeldReleaseRequest r, CancellationToken ct) => Success(await rv.ReleaseHeldAsync(r, ct));
    [HttpGet("instructor-debits"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Debits(CancellationToken ct) => Success(await rv.DebitsAsync(ct));
    [HttpGet("instructor-payouts/statement"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Statement([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct) => Success(await rv.StatementAsync(fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(), ct));
    [HttpGet("instructor/revenue"), Authorize(Roles = RoleNames.Instructor)] public async Task<ActionResult<ApiResponse<object>>> Revenue([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) => Success(await rv.MyRevenueAsync(from, to, ct));
    [HttpPost("instructor-payouts/calculate"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Calc(PayoutCalcRequest r, CancellationToken ct) => Success(await rv.CalculatePayoutsAsync(r, ct));
    [HttpGet("instructor-payouts"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Payouts([FromQuery] PagedQueryLite q, CancellationToken ct) => Success(await rv.PayoutsAsync(q, ct));
    [HttpPut("instructor-payouts/{id:guid}"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> UpdatePayout(Guid id, PayoutUpdateRequest r, CancellationToken ct) { await rv.UpdatePayoutAsync(id, r, ct); return Done("Updated."); }

    [HttpGet("dashboards/student"), Authorize(Roles = RoleNames.Student)] public async Task<ActionResult<ApiResponse<object>>> DStudent(CancellationToken ct) => Success(await ad.StudentDashboardAsync(ct));
    [HttpGet("dashboards/instructor"), Authorize(Roles = RoleNames.Instructor)] public async Task<ActionResult<ApiResponse<object>>> DInstructor(CancellationToken ct) => Success(await ad.InstructorDashboardAsync(ct));
    [HttpGet("dashboards/admin"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> DAdmin(CancellationToken ct) => Success(await ad.AdminDashboardAsync(ct));
    [HttpGet("reports/{kind}/export"), Authorize(Roles = $"{Ta},{RoleNames.Instructor}")]
    public async Task<IActionResult> Report(string kind, [FromQuery] string? format, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) { var (b, t, n) = await ad.ExportAsync(kind, format, from, to, ct); return File(b, t, n); }
    [HttpGet("audit-logs"), Authorize(Roles = $"{Ta},{RoleNames.SuperAdmin}")]
    public async Task<ActionResult<ApiResponse<object>>> Audit([FromQuery] Guid? userId, [FromQuery] string? entity, [FromQuery] string? action, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] PagedQueryLite q, CancellationToken ct) => Success(await ad.AuditLogsAsync(userId, entity, action, from, to, q, ct));
    [HttpGet("recycle-bin"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Bin([FromQuery] string? entity, CancellationToken ct) => Success(await ad.RecycleBinAsync(entity, ct));
    [HttpPost("recycle-bin/{entity}/{id:guid}/restore"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> Restore(string entity, Guid id, CancellationToken ct) { await ad.RestoreAsync(entity, id, ct); return Done("Restored."); }

    [HttpGet("api-keys"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Keys(CancellationToken ct) => Success(await wh.ApiKeysAsync(ct));
    [HttpPost("api-keys"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> CreateKey(ApiKeyRequest r, CancellationToken ct) => Success(await wh.CreateApiKeyAsync(r, ct));
    [HttpDelete("api-keys/{id:guid}"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> Revoke(Guid id, CancellationToken ct) { await wh.RevokeApiKeyAsync(id, ct); return Done("Revoked."); }
    [HttpGet("webhooks"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Hooks(CancellationToken ct) => Success(await wh.ListAsync(ct));
    [HttpPost("webhooks"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> CreateHook(WebhookRequest r, CancellationToken ct) => Success(await wh.CreateAsync(r, ct));
    [HttpPut("webhooks/{id:guid}"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> UpdateHook(Guid id, WebhookRequest r, CancellationToken ct) { await wh.UpdateAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("webhooks/{id:guid}"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> DeleteHook(Guid id, CancellationToken ct) { await wh.DeleteAsync(id, ct); return Done("Deleted."); }
    [HttpGet("webhooks/{id:guid}/deliveries"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object>>> Deliveries(Guid id, [FromQuery] PagedQueryLite q, CancellationToken ct) => Success(await wh.DeliveriesAsync(id, q, ct));
    [HttpPost("webhooks/deliveries/{id:guid}/retry"), Authorize(Roles = Ta)] public async Task<ActionResult<ApiResponse<object?>>> Retry(Guid id, CancellationToken ct) { await wh.RetryAsync(id, ct); return Done("Retry queued."); }
}
