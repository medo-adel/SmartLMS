# Phase 4: Monetization, local payments and enterprise features

54 of 54 Phase 4 operations in `docs/api-contract.json` are present in the running Swagger document (method and path compared, parameter names normalized).
Extra routes not in the contract: `GET /manual-payments/{id}/receipt`, `GET /instructor-payouts`, `GET /integration/orders`, `GET /integration/enrollments` (X-Api-Key + X-Tenant-Id).

## Setup
First start (empty SQL Server; PowerShell, from `backend`):
```powershell
dotnet build SmartLMS.sln
cd src\SmartLMS.API
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<random string, 32+ characters>"
cd ..\..
dotnet ef database update --project src\SmartLMS.Infrastructure --startup-project src\SmartLMS.API
dotnet run --project src\SmartLMS.API --launch-profile https
```
Upgrade from the Phase 3 build: stop the API, `dotnet build SmartLMS.sln`, then the same `dotnet ef database update ...` command. It applies the Phase 4 migrations (`PayoutJobType`, `TenantInstructorShare`, `SubscriptionAllocation`, `RefundAccounting`); or run `docs/ef-migration.sql` (idempotent) in SSMS. Tests: `dotnet test SmartLMS.sln` (SQLite, no SQL Server needed). Neither the migrations nor the SQL Server locking have been run on a live SQL Server here.

## Configuration keys (no secrets in the repo; use user-secrets or environment variables)
- `Payments:Gateways:Paymob:Enabled`, `:Secret` (HMAC key), `:CheckoutUrlTemplate` (hosted checkout URL with `{reference}`, `{amount}`, `{currency}`). Same shape for Fawry and Stripe.
`Payments:VoucherExportRetentionDays`, `Payments:MaxVouchersPerBatch`.
- `Webhooks:AllowPrivateTargets`, `Webhooks:AllowHttp` (both false by default; the SSRF guard refuses private IPs and non-https).
- `RateLimiting:VoucherPermitsPerMinute` (default 20 per user).

## Paymob (real adapter, not yet run against Paymob)
Implemented from Paymob's documentation (checked 24 Nov 2026):
- Create Intention: `POST {BaseUrl}/v1/intention/`, header `Authorization: Token <SecretKey>`, amount in cents, `payment_methods:[IntegrationId]`, `billing_data` (phone required), `special_reference` = our payment reference, `notification_url`. https://developers.paymob.com/paymob-docs/developers/intention-apis/create-intention
- Checkout redirect: `{BaseUrl}/unifiedcheckout/?publicKey=...&clientSecret=...`. https://developers.paymob.com/paymob-docs/developers/checkout-experiences/unified-checkout-redirection
- Callback: `POST /api/v1/payments/gateways/paymob/webhook?hmac=...` with body `{"obj":{...}}`. HMAC-SHA512 (key = HMAC Secret) over the documented field order concatenated without separators, compared in constant time. The order is matched on `obj.order.merchant_order_id`, the amount on `amount_cents`/100, and the payment counts as paid only if success=true and pending, voided, refunded and error_occured are all false. https://developers.paymob.com/paymob-docs/developers/webhook-callbacks-and-hmac/hmac/hmac-transaction-callback
- Keys (user-secrets or environment variables only): `Payments:Gateways:Paymob:Enabled`, `PublicKey`, `SecretKey`, `HmacSecret`, `IntegrationId`, `BaseUrl` (default https://accept.paymob.com), `NotificationUrl` (public HTTPS URL of the webhook), `RedirectionUrl`.
- There is no fallback: if any of PublicKey, SecretKey, HmacSecret or IntegrationId is missing, Paymob reports GATEWAY_NOT_CONFIGURED. The generic adapter below is only for Fawry and Stripe.
- Tests use a fake HTTP handler and the documented worked example for the HMAC string. Nothing was sent to Paymob. Needed from the merchant before a real sandbox run: Paymob test Public Key, Secret Key, HMAC Secret, an online card Integration ID, and a public HTTPS callback URL (tunnel or test host).
- No billing data is invented. Checkout returns 400 BILLING_INFO_REQUIRED if the user has no name, email or phone; only first_name, last_name (if present), email and phone_number are sent. Whether Paymob also requires address fields is UNVERIFIED and needs the merchant test account.

## Generic gateway callback (Fawry, Stripe, )
`POST /api/v1/payments/gateways/{gateway}/webhook` with `X-Tenant-Id` and `X-Signature` = HMAC-SHA256 hex of the raw body with the gateway `Secret`.
Body: `{"reference","status":"succeeded|failed","amount","currency"}`. Wrong signature 401, wrong amount or currency marks the payment failed, replays change nothing. Fawry and Stripe have no provider-specific mapping.

## Limits (real)
- Fawry and Stripe are generic config-driven adapters only. Paymob is implemented per its docs but untested against Paymob's sandbox.
- The Paymob intention call runs inside the checkout database transaction. A slow provider holds it open.
- Not run on live SQL Server or Windows. Tests use SQLite.
- The coupon redemption limit uses a per-process lock: a single instance only. Multi-instance needs a database-level guard.
- Report export is capped at 20,000 rows (413 above). The async path for large exports is not built.
- A Revoked enrollment is not reactivated by a purchase.
- Instructor payouts count course and lesson lines only. Bundle, plan and path lines are not attributed to instructors.
- Voucher plain codes exist only in the exported CSV (hashed in the database); the export file is deleted after the retention window.
- Receipts: PNG, JPEG or PDF by content, 5 MB max. Stored locally, no virus scan.

## Instructor revenue share (policy set by the user's decision)
- There is no built-in percentage. A TenantAdmin sets the academy default (`PUT /instructor-revenue/default`, `{percent}`) and optional per course and instructor overrides (`PUT /courses/{id}/instructor-share`, `{instructorId, percent}`); `GET /instructor-revenue/shares` lists them. All three are extra routes outside the contract, TenantAdmin only, audited.
- Until a default or an override exists, revenue reports and payout calculation return 409 REVENUE_SHARE_NOT_CONFIGURED. Payouts already calculated keep their stored amount when a share changes.
- Storage: nullable `Tenants.InstructorSharePercent` (migration `TenantInstructorShare`) and `InstructorRevenueShares`.
- Decided by the user (via the main agent): fees and affiliate commission are absorbed by the academy; refunds reverse instructor earnings proportionally (see Refunds). Still undecided: what happens to revenue HELD because no percentage was recorded at purchase, and the numeric thresholds below.
- Platform commission, if wanted, is a separate SuperAdmin concern and is not built.

## Coupon limits across instances
`RecheckCouponLimitsAsync` takes a per-coupon `sp_getapplock` inside the checkout transaction on SQL Server and recounts redemptions. Unverified on a live SQL Server; SQLite tests cover the in-process path.

## Bundle, path and subscription allocation (proposed by the assistant in Telegram 12289935 and 12551338; the user agreed in messages 12544274 and 12560429, as relayed by the main agent)
- Everything below is frozen at checkout, per order line and covered course: list price, non-video allowance (plans), and which instructors earn at what percentage (`OrderItemAllocations`, `OrderItemShares`). Only the percentages recorded at purchase are ever used. If none was configured at purchase, or a course was added to a plan afterwards, that revenue is HELD (reported as `heldNoRecordedShare` in the payout statement), never paid from today's setting and never guessed; releasing it needs an explicit TenantAdmin/owner decision that is not built. Courses added to a plan later also earn 0 non-video.
- Bundles and learning paths: the stored list price of every course inside the line is the split weight. The line's net amount is later split by those frozen weights (equal split if all are 0), then the course's instructor share applies. Later price or bundle edits do not move money already earned (tested). Lines without a snapshot (none are created by this code) would be reported as unallocated.
- Subscriptions: a paid plan line is earned evenly over its paid window (paid date plus billing period; cumulative rounding so any cut of periods adds up to the exact amount, tested). A cancellation only stops renewal, so the full paid window is recognised (tested). Each period's earned amount is split across the courses the subscriber learned in that period by qualified learning time, then the instructor share applies. A period with no qualified learning leaves its amount unallocated (reported, not paid out).
- Qualified time: lessons completed in the period, not free previews, not courses the subscriber bought directly (course, bundle or path).
  - Video and audio use the duration measured from the stored file (`FileAssets.MeasuredDurationSeconds`, set at upload by `MediaDuration`), never the typed `DurationSeconds` (tested: a typed 9999 s is ignored). Counted only if watched seconds are at least the configured minimum percentage of the measured duration, then min(watched, measured). Formats measured: mp4, m4a, mov (movie header) and wav. webm, mp3, unreadable files and external-URL videos are not measured and earn 0.
  - Re-upload and splitting invariance: the same file bytes (SHA-256) count once per student for life, whatever lesson or course they were re-uploaded under (tested). Splitting a video into parts of the same total length earns the same measured time.
  - Non-video lessons: the TenantAdmin approves one number per course (`NonVideoCourseSeconds`) for all its non-video content together. The allowance and the published non-video lesson count are frozen on each plan line at purchase; the k-th lifetime completion earns weight/count only while k <= count, so adding or splitting lessons or revising the weight later never changes earnings of a purchase already made (tested). No weight means 0.
- Controls (TenantAdmin, audited, extra routes): `GET/PUT /instructor-revenue/subscription-controls` (`minWatchPercent`, `minAverageLessonSeconds`, `jumpAlertPercent`), `PUT /courses/{id}/learning-weight` (`nonVideoCourseSeconds`). No numeric default exists: if a subscription earns in the period and `minWatchPercent` is unset, the statement and payout calculation return 409 SUBSCRIPTION_ALLOCATION_NOT_CONFIGURED. The two flag thresholds are off until set.
- Review: `GET /instructor-payouts/statement?fromUtc&toUtc` (TenantAdmin) shows per instructor earnings by source, qualified minutes, lessons counted, previous-period subscription earnings, flags SHORT_LESSONS and SUBSCRIPTION_EARNINGS_JUMP, and unallocated subscription revenue. Payouts are then calculated and approved by the existing flow.
- Fees: per the user (Telegram 12664689, relayed by the main agent) the academy absorbs Paymob/payment fees and affiliate commission from its own share. Instructor earnings are therefore computed on the actual sale value (coupon discount already deducted once) with no fee or commission deducted. Refund policy is undecided: refunds are not clawed back.
- Limits: WatchedSeconds is reported by the client app (each heartbeat is capped at 5 minutes server-side); there is no per-day activity log, so no daily cap exists. Not run on live SQL Server.

## Refunds (accounting policy approved by the user in Telegram 12706749, relayed by the main agent)

- `POST /api/v1/orders/{orderId}/refunds` (TenantAdmin, `{amount, reference, reason?}`; the reference, for example the gateway refund id, is required and unique per order, so repeating a request returns the original record, and reusing it with another amount is 409) records a refund that happened outside the system. It moves NO money and does not touch the gateway, wallet or access. Total refunds of an order cannot exceed its total.
- Earnings not yet in a calculated payout are reduced by the refunded share of the order (refunded / order total), for every line and every instructor of that order, including subscription revenue recognised later.
- Earnings already inside a calculated (not cancelled) payout are NOT withdrawn: an `InstructorDebits` row (proportional to the refund) is recorded and the next payout calculations subtract it (`InstructorDebitApplications` keeps the trail). `GET /instructor-debits` lists them and the outstanding total. A payout of 0 can result when a debit exceeds new earnings; the rest keeps carrying forward.
- Cancelling a payout gives the debits it absorbed back, and removes the debits that were created because that payout was refunded (its recalculation already nets the refund). A payout whose refund debit was already carried to a later payout cannot be cancelled (409 DEBIT_ALREADY_APPLIED; tested).
- Limits: a debit is computed from the earnings the payout period recomputes at refund time; subscription refunds reduce future recognition from the next calculated period, and an already-calculated period is handled by the debit. Nothing handles refunds on dispute chargebacks, and no automatic refund through Paymob exists.
- Immutable inputs: every calculated payout stores `InstructorPayoutLines` (gross earned before refunds and paid earned after the refunds that existed then, per order line and course). Refund debits are computed only from those rows, never from recomputed learning activity or settings. A payout's debits for one order can never exceed what that order earned inside it (tested across two calculated subscription periods: debits add up to exactly the 1000 earned).
- Concurrency: payout calculation and refund recording both take a tenant row lock inside a transaction (then the order row for refunds), so a refund is either inside the payout's numbers or becomes a debit against it. Test: refund racing a calculation, 3 rounds, net is always 30 of 60 (payout minus outstanding debit). Parallel refunds of one order cannot over-refund. Verified on SQLite only; SQL Server lock behaviour is not verified live.

## Held revenue (user decision Telegram 12773511, relayed by the main agent)

Revenue with no recorded percentage at purchase (or from a course added to a plan afterwards) stays HELD until the TenantAdmin reviews it and sets a percentage explicitly. `GET /instructor-revenue/held?fromUtc&toUtc` lists it; `PUT /instructor-revenue/held {orderItemId, courseId, instructorId, percent}` sets it (audited, once, never changeable afterwards). Setting a tenant default later does not release it. If a payout of that instructor already covers the sale, the release is flagged late and settled once in the NEXT payout calculated (earnings of that line up to the end of that period, in one amount); payouts already made, even paid ones, are never rewritten. Cancelling the settling payout returns the release to waiting. A late-released subscription line is allocated period by period: the cuts are the edges of the instructor's live payouts plus the end of the settling period, each slice recognised evenly and split by learning inside that slice, exactly like a normal calculation (tested: a 1000 plan released late settles to exactly 1000 while the earlier 100 payout is untouched). It does not appear in revenue views until settled. For a course added to a plan after purchase the TenantAdmin may pass `nonVideoSeconds` and `nonVideoLessonCount` together in the release request; they are stored as that line's frozen allowance (not changeable afterwards, 409 if one was recorded at purchase). Without them the course earns 0 non-video, and nothing is taken from today's settings. The input validation is tested; the earnings effect of a release-time allowance reuses the purchase-time allowance code path and has no separate test.
