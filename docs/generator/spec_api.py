"""Single source of truth for the SmartLMS .NET API contract (base path /api/v1).

Row: (METHOD, path, roles, summary, phase)   roles: Public | Auth | comma list of SA TA IN TAS ST PA
SA=SuperAdmin TA=TenantAdmin IN=Instructor TAS=TeachingAssistant ST=Student PA=Parent
Paths ending in a verb after a resource id are actions. Every list endpoint takes ?page=&pageSize= (max 100) and returns PagedResult<T>.
Phase = the phase that implements the endpoint. In Phase 1 only GET /health exists.
"""

MODULES = []


def M(title, intro, rows):
    MODULES.append(dict(title=title, intro=intro, rows=rows))


M("Platform", "Operational endpoints.", [
    ("GET", "/health", "Public", "Liveness check. Returns ApiResponse<HealthStatus>.", 1),
    ("GET", "/tenants/current/logo", "Public", "The tenant's logo image (404 when none).", 2),
    ("GET", "/tenants/current/branding", "Public", "Branding for the resolved tenant (logo, colors, default language, feature flags) so the app can theme itself before login.", 2),
])

M("Auth, Devices and Users", "Login endpoints are rate limited. Every request that follows login carries Authorization, X-Tenant-Id and X-Device-Id.", [
    ("POST", "/auth/register", "Public", "Register a student (or a parent when the tenant allows it). Sends an email/OTP verification.", 2),
    ("POST", "/auth/login", "Public", "Email + password + device fingerprint. Returns access token, refresh token, user. Fails with DEVICE_LIMIT_REACHED when the device cap is hit.", 2),
    ("POST", "/auth/refresh", "Public", "Rotate refresh token. Reuse of a used token revokes the chain.", 2),
    ("POST", "/auth/logout", "Auth", "Revoke the current refresh token.", 2),
    ("POST", "/auth/verify-email", "Public", "Verify email with the 6-digit OTP sent at registration (token links are not used).", 2),
    ("POST", "/auth/otp/request", "Public", "Request an OTP (email verification, phone, login, password reset).", 2),
    ("POST", "/auth/otp/verify", "Public", "Verify an OTP.", 2),
    ("POST", "/auth/forgot-password", "Public", "Always returns success, whether or not the account exists.", 2),
    ("POST", "/auth/reset-password", "Public", "Reset with the 6-digit OTP from forgot-password. Revokes all refresh tokens.", 2),
    ("POST", "/auth/social/{provider}", "Public", "DEFERRED hook. Returns 501 NOT_IMPLEMENTED in Phase 2: provider keys and token validation are not built. Not counted as implemented.", 2),
    ("POST", "/auth/biometric/enable", "Auth", "Mark biometric unlock on for this device. The biometric check itself happens on the device and unlocks the stored refresh token.", 2),
    ("GET", "/me", "Auth", "Current user profile and roles.", 2),
    ("PUT", "/me", "Auth", "Update own profile and preferred language.", 2),
    ("POST", "/me/change-password", "Auth", "Change password; revokes other refresh tokens.", 2),
    ("GET", "/me/devices", "Auth", "List own registered devices.", 2),
    ("DELETE", "/me/devices/{deviceId}", "Auth", "Remove one of own devices (counts toward any reset limits the tenant sets).", 2),
    ("GET", "/users", "SA,TA,IN", "Search users in the tenant. Instructors see only students enrolled in courses they own (none until Phase 3 creates enrollments); other accounts answer 404.", 2),
    ("POST", "/users", "TA", "Create a user with roles.", 2),
    ("GET", "/users/{id}", "SA,TA,IN", "User detail.", 2),
    ("PUT", "/users/{id}", "TA", "Update user.", 2),
    ("PUT", "/users/{id}/roles", "TA", "Replace roles. SuperAdmin role is assignable only by SuperAdmin.", 2),
    ("POST", "/users/{id}/deactivate", "TA", "Deactivate (soft). Revokes tokens.", 2),
    ("GET", "/users/{id}/devices", "TA,IN", "List a student's devices.", 2),
    ("POST", "/users/{id}/devices/reset", "TA,IN", "Reset a student's devices. Writes DeviceResetLogs and AuditLogs.", 2),
])

M("Tenants and Platform Admin", "SuperAdmin manages tenants. TenantAdmin manages their own tenant's settings.", [
    ("GET", "/tenants", "SA", "List tenants.", 2),
    ("POST", "/tenants", "SA", "Create tenant with first TenantAdmin.", 2),
    ("GET", "/tenants/{id}", "SA", "Tenant detail.", 2),
    ("PUT", "/tenants/{id}", "SA", "Update tenant, including device caps.", 2),
    ("POST", "/tenants/{id}/support-access", "SA", "Grant the calling SuperAdmin time-boxed access inside the tenant (default 60 min, max 480). Writes a SupportAccess membership and an audit row. Needed before any tenant route can be called with X-Tenant-Id.", 2),
    ("DELETE", "/tenants/{id}/support-access", "SA", "Revoke the caller's support access to the tenant.", 2),
    ("PUT", "/tenant/settings", "TA", "Update own tenant's branding, languages, currency.", 2),
    ("POST", "/tenant/logo", "TA", "Upload logo (multipart, field 'file'). PNG, JPEG or WebP, max 2 MB, type checked from the bytes (SVG refused). Stored under the tenant's own folder; sets LogoUrl to /api/v1/tenants/current/logo.", 2),
    ("GET", "/tenant/feature-flags", "TA", "List flags.", 2),
    ("PUT", "/tenant/feature-flags/{key}", "TA", "Enable or disable a feature for the tenant.", 2),
])

M("Catalog and Course Builder", "Catalog reads are public per tenant only when the tenant enables a public catalog; otherwise they need login.", [
    ("GET", "/categories", "Public", "Category tree.", 3),
    ("POST", "/categories", "TA", "Create category.", 3),
    ("PUT", "/categories/{id}", "TA", "Update category.", 3),
    ("DELETE", "/categories/{id}", "TA", "Soft delete.", 3),
    ("GET", "/courses", "Public", "Search and filter published courses (category, level, language, price, text).", 3),
    ("GET", "/courses/{id}", "Public", "Course detail and outline (sections, lesson titles, free previews).", 3),
    ("GET", "/courses/mine", "IN,TAS", "Courses I own or staff.", 3),
    ("POST", "/courses", "IN,TA", "Create draft course.", 3),
    ("PUT", "/courses/{id}", "IN,TA", "Update course.", 3),
    ("DELETE", "/courses/{id}", "IN,TA", "Soft delete.", 3),
    ("POST", "/courses/{id}/submit-for-approval", "IN", "Submit draft for admin approval.", 3),
    ("POST", "/courses/{id}/approve", "TA", "Approve and publish.", 3),
    ("POST", "/courses/{id}/reject", "TA", "Reject with comment.", 3),
    ("GET", "/courses/{id}/staff", "IN,TA", "List co-instructors and teaching assistants.", 3),
    ("PUT", "/courses/{id}/staff", "IN,TA", "Replace staff list.", 3),
    ("PUT", "/courses/{id}/prerequisites", "IN,TA", "Replace prerequisite courses.", 3),
    ("POST", "/courses/{courseId}/sections", "IN,TA", "Create section (with optional drip rule).", 3),
    ("PUT", "/sections/{id}", "IN,TA", "Update section.", 3),
    ("DELETE", "/sections/{id}", "IN,TA", "Soft delete.", 3),
    ("PUT", "/courses/{courseId}/sections/order", "IN,TA", "Reorder sections.", 3),
    ("POST", "/sections/{sectionId}/lessons", "IN,TA", "Create lesson of any type.", 3),
    ("GET", "/lessons/{id}", "ST,IN,TA,TAS", "Lesson content. Enforces enrollment or lesson grant, drip rules and prerequisites; returns 403 with reason otherwise.", 3),
    ("PUT", "/lessons/{id}", "IN,TA", "Update lesson.", 3),
    ("DELETE", "/lessons/{id}", "IN,TA", "Soft delete.", 3),
    ("PUT", "/sections/{sectionId}/lessons/order", "IN,TA", "Reorder lessons.", 3),
    ("POST", "/lessons/{id}/media", "IN,TA", "Upload lesson video, audio or PDF (multipart, resumable chunks). Triggers AI ingest if the tenant enables it.", 3),
    ("GET", "/lessons/{id}/stream", "ST,IN,TA,TAS", "Authorized media stream or signed short-lived URL. Records a PlaybackSession.", 3),
    ("POST", "/lessons/{id}/resources", "IN,TA", "Attach downloadable resource.", 3),
    ("GET", "/lessons/{id}/resources/{resourceId}/download", "ST,IN,TA,TAS", "Download resource (authorized).", 3),
    ("PUT", "/lessons/{id}/chapters", "IN,TA", "Replace chapter markers.", 3),
    ("PUT", "/lessons/{id}/slides", "IN,TA", "Replace interactive slide content.", 3),
    ("GET", "/lessons/{id}/interactive-quizzes", "ST,IN,TA,TAS", "Quiz pause points for the player.", 3),
    ("PUT", "/lessons/{id}/interactive-quizzes", "IN,TA", "Replace quiz pause points.", 3),
    ("POST", "/interactive-quizzes/{id}/answer", "ST", "Answer an embedded quiz. Returns correctness and whether playback may continue.", 3),
    ("GET", "/learning-paths", "Public", "List published learning paths.", 3),
    ("GET", "/learning-paths/{id}", "Public", "Path detail with ordered courses.", 3),
    ("POST", "/learning-paths", "IN,TA", "Create path.", 3),
    ("PUT", "/learning-paths/{id}", "IN,TA", "Update path and course order.", 3),
    ("DELETE", "/learning-paths/{id}", "TA", "Soft delete.", 3),
])

M("Learning, Progress and Engagement", "Student-facing.", [
    ("POST", "/courses/{id}/enroll", "ST", "Enroll in a free course or one already entitled by purchase/voucher/subscription.", 3),
    ("GET", "/me/enrollments", "ST", "My courses with progress.", 3),
    ("POST", "/learning-paths/{id}/enroll", "ST", "Enroll in a path.", 3),
    ("PUT", "/lessons/{id}/progress", "ST", "Heartbeat: last position, watched seconds, completion, speed. Idempotent.", 3),
    ("GET", "/courses/{id}/progress", "ST", "My progress in a course.", 3),
    ("GET", "/lessons/{id}/notes", "ST", "My notes on a lesson.", 3),
    ("POST", "/lessons/{id}/notes", "ST", "Add timestamped note.", 3),
    ("PUT", "/notes/{id}", "ST", "Edit note.", 3),
    ("DELETE", "/notes/{id}", "ST", "Delete note.", 3),
    ("GET", "/lessons/{id}/bookmarks", "ST", "My bookmarks.", 3),
    ("POST", "/lessons/{id}/bookmarks", "ST", "Add bookmark.", 3),
    ("DELETE", "/bookmarks/{id}", "ST", "Delete bookmark.", 3),
    ("POST", "/study-sessions", "ST", "Record a study session for hours and streak tracking.", 3),
    ("GET", "/courses/{courseId}/threads", "ST,IN,TA,TAS", "Course forum and lesson Q&A threads (filter by lessonId).", 3),
    ("POST", "/courses/{courseId}/threads", "ST,IN,TAS", "Start thread or question.", 3),
    ("GET", "/threads/{id}", "ST,IN,TA,TAS", "Thread with posts.", 3),
    ("POST", "/threads/{id}/posts", "ST,IN,TAS", "Reply.", 3),
    ("POST", "/threads/{id}/upvote", "ST,IN,TAS", "Toggle upvote.", 3),
    ("POST", "/posts/{id}/upvote", "ST,IN,TAS", "Toggle upvote.", 3),
    ("POST", "/threads/{id}/pin", "IN,TAS", "Pin a thread or mark an answer as instructor-pinned.", 3),
    ("GET", "/courses/{courseId}/live-sessions", "ST,IN,TA,TAS", "Scheduled live classes.", 3),
    ("POST", "/courses/{courseId}/live-sessions", "IN,TAS", "Schedule a live class with a Zoom, Meet or Jitsi link.", 3),
    ("POST", "/live-sessions/{id}/join", "ST", "Record attendance and return the join URL.", 3),
    ("GET", "/live-sessions/{id}/attendance", "IN,TA,TAS", "Attendance list.", 3),
    ("GET", "/announcements", "Auth", "Announcements visible to me.", 3),
    ("POST", "/announcements", "IN,TA", "Publish announcement (also pushed via SignalR and FCM).", 3),
    ("GET", "/notifications", "Auth", "My notifications.", 3),
    ("POST", "/notifications/{id}/read", "Auth", "Mark read.", 3),
    ("POST", "/notifications/push-token", "Auth", "Register FCM token for this device.", 3),
    ("GET", "/chat/rooms", "Auth", "My chat rooms.", 3),
    ("GET", "/chat/rooms/{id}/messages", "Auth", "Message history (cursor paging).", 3),
    ("POST", "/chat/rooms/{id}/messages", "Auth", "Send message (SignalR is the live path; this is the fallback).", 3),
    ("GET", "/gamification/me", "ST", "My XP, level, streak and badges.", 3),
    ("GET", "/gamification/leaderboard", "ST,IN,TA", "Course or global leaderboard (?courseId=).", 3),
])

M("Assessments and Assignments", "Server-side grading. Correct answers are never sent to the client before submission.", [
    ("GET", "/courses/{courseId}/question-banks", "IN,TA,TAS", "List question banks.", 3),
    ("POST", "/question-banks", "IN,TA,TAS", "Create bank.", 3),
    ("GET", "/question-banks/{id}/questions", "IN,TA,TAS", "Questions with filters (type, difficulty, topic).", 3),
    ("POST", "/question-banks/{id}/questions", "IN,TA,TAS", "Add question (all seven types).", 3),
    ("PUT", "/questions/{id}", "IN,TA,TAS", "Update question.", 3),
    ("DELETE", "/questions/{id}", "IN,TA", "Soft delete.", 3),
    ("POST", "/question-banks/{id}/import", "IN,TA", "Import questions from CSV or JSON.", 3),
    ("GET", "/courses/{courseId}/exams", "ST,IN,TA,TAS", "Exams and quizzes visible to me.", 3),
    ("POST", "/courses/{courseId}/exams", "IN,TA", "Create exam with pool rules, timer, attempts, passing score, proctoring flags.", 3),
    ("PUT", "/exams/{id}", "IN,TA", "Update exam.", 3),
    ("POST", "/exams/{id}/publish", "IN,TA", "Publish.", 3),
    ("POST", "/exams/{id}/attempts", "ST", "Start attempt. Freezes the question set, starts the server-side timer. Rejects past the attempt limit.", 3),
    ("GET", "/attempts/{id}", "ST", "Questions for the attempt (no correct answers) and remaining time.", 3),
    ("PUT", "/attempts/{id}/answers", "ST", "Autosave answers.", 3),
    ("POST", "/attempts/{id}/events", "ST", "Report tab switch or focus loss. Stored as ProctoringEvents. May void the attempt if the exam sets a limit.", 3),
    ("POST", "/attempts/{id}/submit", "ST", "Submit. Auto-grades objective questions; essays and short answers wait for manual grading.", 3),
    ("GET", "/attempts/{id}/result", "ST,IN,TA,TAS", "Result and feedback, subject to the exam's review policy.", 3),
    ("GET", "/exams/{id}/attempts", "IN,TA,TAS", "All attempts, scores and proctoring flags.", 3),
    ("PUT", "/attempt-answers/{id}/grade", "IN,TA,TAS", "Manual grade. Audited.", 3),
    ("GET", "/courses/{courseId}/assignments", "ST,IN,TA,TAS", "Assignments.", 3),
    ("POST", "/courses/{courseId}/assignments", "IN,TA", "Create with rubric, group and peer-review settings.", 3),
    ("PUT", "/assignments/{id}", "IN,TA", "Update.", 3),
    ("POST", "/assignments/{id}/groups", "ST,IN", "Create or join a team.", 3),
    ("POST", "/assignments/{id}/submissions", "ST", "Submit text, link and files (new version on resubmission).", 3),
    ("GET", "/assignments/{id}/submissions", "IN,TA,TAS", "Submissions with grading state.", 3),
    ("GET", "/submissions/{id}", "ST,IN,TA,TAS", "Submission detail (students see only their own or their team's).", 3),
    ("PUT", "/submissions/{id}/grade", "IN,TA,TAS", "Rubric scores and feedback. Audited.", 3),
    ("POST", "/submissions/{id}/comments", "IN,TA,TAS", "Inline comment anchored to text range or file page.", 3),
    ("POST", "/submissions/{id}/return", "IN,TA,TAS", "Return for resubmission.", 3),
    ("POST", "/assignments/{id}/peer-reviews/assign", "IN,TA", "Create peer-review assignments (async job).", 3),
    ("GET", "/peer-reviews/mine", "ST", "Reviews I must write.", 3),
    ("PUT", "/peer-reviews/{id}", "ST", "Submit review with rubric scores.", 3),
])

M("Parent Portal and Certificates", "A parent sees only students with an Active ParentStudentLink.", [
    ("POST", "/parent/links", "PA", "Request a link using the student's link code.", 3),
    ("POST", "/students/me/link-codes", "ST", "Create a one-time code the student gives to a parent.", 3),
    ("PUT", "/parent/links/{id}", "TA,IN", "Approve or revoke a link.", 3),
    ("GET", "/parent/children", "PA", "Linked students.", 3),
    ("GET", "/parent/children/{studentId}/summary", "PA", "Attendance, grades, weekly study hours, risk alerts.", 3),
    ("GET", "/parent/alerts", "PA", "Low-performance alerts.", 3),
    ("POST", "/certificates/issue", "TA,IN", "Issue a certificate manually. Automatic issuing runs on course or path completion.", 3),
    ("GET", "/certificates/mine", "ST", "My certificates.", 3),
    ("GET", "/certificates/{id}/pdf", "ST,TA,IN", "Download PDF (with QR code).", 3),
    ("GET", "/certificates/verify/{verificationCode}", "Public", "Public verification: holder name, course, issue date, status. No email or phone.", 3),
])

M("Commerce, Wallet and Vouchers", "Money endpoints are idempotent (Idempotency-Key header) and audited.", [
    ("GET", "/cart", "ST", "My cart.", 4),
    ("POST", "/cart/items", "ST", "Add course, lesson, bundle, plan or path.", 4),
    ("DELETE", "/cart/items/{id}", "ST", "Remove.", 4),
    ("POST", "/cart/coupon", "ST", "Apply a coupon; validates expiry, limits and scope.", 4),
    ("POST", "/orders/checkout", "ST", "Create order from cart. Body picks the payment method: gateway, wallet, voucher or manual.", 4),
    ("GET", "/orders", "ST", "My orders.", 4),
    ("GET", "/orders/{id}", "ST,TA", "Order detail.", 4),
    ("GET", "/orders/{id}/invoice", "ST,TA", "Invoice PDF.", 4),
    ("POST", "/payments/gateways/{gateway}/webhook", "Public", "Gateway callback (Paymob, Fawry, Stripe). Verified by signature before any state change.", 4),
    ("GET", "/bundles", "Public", "Bundles.", 4),
    ("POST", "/bundles", "TA", "Create bundle.", 4),
    ("GET", "/subscription-plans", "Public", "Plans.", 4),
    ("POST", "/subscription-plans", "TA", "Create plan.", 4),
    ("POST", "/subscriptions", "ST", "Start subscription.", 4),
    ("POST", "/subscriptions/{id}/cancel", "ST", "Cancel at period end.", 4),
    ("POST", "/lessons/{id}/unlock", "ST", "Unlock a pay-per-lesson lesson from wallet balance.", 4),
    ("GET", "/wallet", "ST", "Balance.", 4),
    ("GET", "/wallet/transactions", "ST", "Ledger.", 4),
    ("POST", "/wallet/topup/gateway", "ST", "Start gateway top-up.", 4),
    ("POST", "/wallet/topup/voucher", "ST", "Top up with a voucher code.", 4),
    ("POST", "/vouchers/redeem", "ST", "Redeem a course-access voucher.", 4),
    ("POST", "/voucher-batches", "TA", "Create batch. Returns a BackgroundJob id; the CSV is produced by the job.", 4),
    ("GET", "/voucher-batches", "TA", "List batches and status.", 4),
    ("GET", "/voucher-batches/{id}/export", "TA", "Download the batch CSV (only available after generation, within retention).", 4),
    ("POST", "/voucher-batches/{id}/void", "TA", "Void unused codes.", 4),
    ("POST", "/manual-payments", "ST", "Submit offline transfer proof with receipt image (multipart).", 4),
    ("GET", "/manual-payments/mine", "ST", "My submissions and status.", 4),
    ("GET", "/manual-payments", "TA", "Review queue.", 4),
    ("POST", "/manual-payments/{id}/approve", "TA", "Approve. Completes the order or credits the wallet in one transaction. Audited.", 4),
    ("POST", "/manual-payments/{id}/reject", "TA", "Reject with reason.", 4),
    ("GET", "/coupons", "TA", "List coupons.", 4),
    ("POST", "/coupons", "TA", "Create coupon.", 4),
    ("PUT", "/coupons/{id}", "TA", "Update coupon.", 4),
    ("GET", "/affiliate/me", "ST,IN", "My referral code, link, referrals and commissions.", 4),
    ("POST", "/affiliate/profiles", "TA", "Enable a user as affiliate with a commission percent.", 4),
    ("GET", "/instructor/revenue", "IN", "My revenue share and payouts.", 4),
    ("POST", "/instructor-payouts/calculate", "TA", "Calculate payouts for a period (async job).", 4),
    ("PUT", "/instructor-payouts/{id}", "TA", "Approve or mark paid.", 4),
])

M("Dashboards, Audit, Recycle Bin, Webhooks and API Keys", "Admin and analytics.", [
    ("GET", "/dashboards/student", "ST", "Progress, deadlines, skill radar.", 4),
    ("GET", "/dashboards/instructor", "IN", "Enrollment trends, lesson drop-off heatmap, quiz difficulty, revenue.", 4),
    ("GET", "/dashboards/admin", "TA", "KPIs, approval queues, revenue.", 4),
    ("GET", "/reports/{kind}/export", "TA,IN", "CSV or Excel export (async for large data).", 4),
    ("GET", "/audit-logs", "TA,SA", "Filter by user, entity, action, date.", 4),
    ("GET", "/recycle-bin", "TA", "Soft-deleted items by entity type.", 4),
    ("POST", "/recycle-bin/{entity}/{id}/restore", "TA", "Restore. Audited.", 4),
    ("GET", "/api-keys", "TA", "List keys (prefix only).", 4),
    ("POST", "/api-keys", "TA", "Create key. Full key returned once.", 4),
    ("DELETE", "/api-keys/{id}", "TA", "Revoke.", 4),
    ("GET", "/webhooks", "TA", "List endpoints.", 4),
    ("POST", "/webhooks", "TA", "Create endpoint with event types. Secret returned once.", 4),
    ("PUT", "/webhooks/{id}", "TA", "Update.", 4),
    ("DELETE", "/webhooks/{id}", "TA", "Delete.", 4),
    ("GET", "/webhooks/{id}/deliveries", "TA", "Delivery log.", 4),
    ("POST", "/webhooks/deliveries/{id}/retry", "TA", "Retry a failed delivery.", 4),
    ("GET", "/jobs/{id}", "Auth", "Background job status (Pending, Processing, Completed, Failed). Owner or admin only.", 3),
])

M("AI (through the .NET API)", "The app never calls the Python service. These endpoints check authentication, tenant, enrollment, per-user rate limits and the tenant's ai_tutor feature flag, then forward to the AI service. Long operations return 202 with a job id.", [
    ("POST", "/ai/lessons/{lessonId}/ingest", "IN,TA", "Queue ingest (extract, transcribe, chunk, embed). 202 + job id.", 5),
    ("GET", "/ai/lessons/{lessonId}/index-status", "IN,TA", "Index state.", 5),
    ("POST", "/ai/courses/{courseId}/tutor/chat", "ST,IN", "Course tutor with citations. Answers only from that course's material.", 5),
    ("POST", "/ai/courses/{courseId}/video/semantic-search", "ST,IN", "Find timestamps and snippets across course transcripts.", 5),
    ("POST", "/ai/lessons/{lessonId}/generate-quiz", "IN,TA,TAS", "Generate MCQs, open questions, flashcards. Output is a draft the instructor reviews.", 5),
    ("POST", "/ai/lessons/{lessonId}/summarize", "ST,IN", "Takeaways, key terms, chapters.", 5),
    ("POST", "/ai/lessons/{lessonId}/mindmap", "ST,IN", "Mind map nodes and edges.", 5),
    ("POST", "/ai/submissions/{id}/grade-essay", "IN,TA,TAS", "Provisional score and feedback against the rubric. Never written as the final grade without an instructor action.", 5),
    ("POST", "/ai/assignments/{id}/plagiarism-check", "IN,TA", "Queue batch similarity check. 202 + job id.", 5),
    ("GET", "/ai/submissions/{id}/plagiarism", "IN,TA", "Similarity scores and flagged matches for human review.", 5),
    ("POST", "/ai/courses/{courseId}/voice-tutor", "ST", "Audio in (multipart), text and audio out.", 5),
    ("POST", "/ai/courses/{courseId}/study-plan", "ST", "Day-by-day plan.", 5),
    ("GET", "/ai/students/{studentId}/risk", "IN,TA", "Dropout risk score and suggested interventions (advisory).", 5),
])

M("SignalR Hubs", "Connect at /hubs/{name} with the access token (query string access_token for WebSockets).", [
    ("WS", "/hubs/notifications", "Auth", "Notifications, announcements, job status changes (jobCompleted, jobFailed).", 3),
    ("WS", "/hubs/chat", "Auth", "Room chat. Server checks room membership on join and send.", 3),
    ("WS", "/hubs/live", "Auth", "Live class presence and Q&A.", 3),
])

CONVENTIONS = """

> **Scaffold status.** This document is a design contract. In Phase 1 only `GET /health` is implemented. Listed paths, roles and behaviors for every other endpoint describe what will be built, not what works today. Nothing here is a functional claim until its phase is delivered and tested.
## Conventions

**Base URL.** `https://localhost:7200/api/v1` in development.

**Envelope.** Every JSON response is `ApiResponse<T>`:

```json
{ "success": true, "data": { }, "message": null, "errors": [] }
```

On failure `success` is `false`, `data` is `null`, `message` is a short human message and `errors` lists field messages or machine codes.

**Lists.** `GET` list endpoints accept `page` (default 1) and `pageSize` (default 20, max 100) and return `ApiResponse<PagedResult<T>>` with `items`, `page`, `pageSize`, `totalCount`.

**Headers.**

| Header | When | Purpose |
|---|---|---|
| `Authorization: Bearer <jwt>` | after login | Access token (15 min) |
| `X-Tenant-Id` | always (or tenant subdomain) | Tenant resolution. For authenticated calls it must match the token's tenant claim |
| `X-Device-Id` | always on mobile and web | Device fingerprint hash for device binding |
| `Idempotency-Key` | money and voucher endpoints | Safe retries |
| `Accept-Language` | optional | `en` or `ar` for server messages |

**Status codes.** 200 OK, 201 Created, 202 Accepted (background job), 400 validation or business rule, 401 unauthenticated, 403 forbidden (including device not allowed), 404 not found or not visible to this tenant, 409 conflict, 429 rate limited, 500 unexpected.

**Machine error codes** (in `errors[]`): `DEVICE_LIMIT_REACHED`, `DEVICE_NOT_REGISTERED`, `TENANT_MISMATCH`, `ENROLLMENT_REQUIRED`, `LESSON_LOCKED_DRIP`, `PREREQUISITE_NOT_MET`, `ATTEMPT_LIMIT_REACHED`, `EXAM_TIME_EXPIRED`, `INSUFFICIENT_WALLET_BALANCE`, `COUPON_INVALID`, `VOUCHER_INVALID`, `AI_UNAVAILABLE`.

**Async jobs.** Endpoints that start background work return 202 and `{ "jobId": "..." }`. Poll `GET /jobs/{id}` or listen on `/hubs/notifications`. Status values: `Pending`, `Processing`, `Completed`, `Failed`.

**Roles.** SA SuperAdmin, TA TenantAdmin, IN Instructor, TAS TeachingAssistant, ST Student, PA Parent. `Public` needs no token. `Auth` means any signed-in user. Tenant isolation applies to every role except SA.

**Versioning.** URL prefix `/api/v1`. Breaking changes get a new prefix.

**Webhook events** (outgoing, signed `X-SmartLMS-Signature: sha256=<hmac>`): `enrollment.created`, `course.completed`, `payment.approved`, `certificate.issued`, `voucher.batch.ready`, `assignment.graded`, `exam.submitted`.
"""

AI_SERVICE = """
## Internal AI service contract (Python FastAPI, called only by the .NET API)

Auth: header `X-AI-Service-Key` must equal the shared secret. The service returns 401 otherwise, and rejects everything if no key is configured. Interactive docs at `http://localhost:8001/docs`. Request and response models live in `ai-service/app/schemas.py` (Pydantic v2) and are the source of truth.

| Method | Path | Purpose | Phase 1 state |
|---|---|---|---|
| GET | /health | Liveness (no key needed) | works |
| POST | /ai/ingest | Extract, chunk, transcribe, embed | typed route, returns 501 |
| POST | /ai/tutor/chat | RAG tutor with citations | typed route, returns 501 |
| POST | /ai/video/semantic-search | Transcript search with timestamps | typed route, returns 501 |
| POST | /ai/generate-quiz | MCQs, open questions, flashcards | typed route, returns 501 |
| POST | /ai/summarize | Takeaways, key terms, chapters | typed route, returns 501 |
| POST | /ai/mindmap | Nodes and edges | typed route, returns 501 |
| POST | /ai/grade-essay | Provisional rubric grade | typed route, returns 501 |
| POST | /ai/plagiarism-check | Embedding similarity vs other submissions | typed route, returns 501 |
| POST | /ai/voice-tutor | STT, answer, TTS (multipart) | typed route, returns 501 |
| POST | /ai/study-plan | Day-by-day schedule | typed route, returns 501 |
| POST | /ai/predict-risk | Dropout risk score | typed route, returns 501 |
"""
