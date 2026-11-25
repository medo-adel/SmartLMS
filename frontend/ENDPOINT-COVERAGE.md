# SmartLMS endpoint-to-UI coverage audit

Generated 2026-11-25 by walking every [Http*] route in SmartLMS.API/Controllers (242 endpoints, per-controller route prefixes resolved per class) and matching each against the delivered frontend source (src/app, excluding specs).
**Covered: 229 · No UI: 13** (no-UI rows are classified at the end)

The backend update package adds 3 new read endpoints that the frontend also covers (not part of the original 242): `GET /exams/{id}` (exam edit prefill), `GET /courses/{id}/prerequisites` (prerequisite prefill), `GET /courses/{courseId}/learning-weight` (current weight display).


## AiController
- [ ] `POST /ai/assignments/{id}/plagiarism-check` — NO UI
- [x] `POST /ai/courses/{id}/study-plan` — core: core/services/learning.service.ts
- [x] `POST /ai/courses/{id}/tutor/chat` — core: core/services/learning.service.ts
- [x] `POST /ai/courses/{id}/video/semantic-search` — core: core/services/learning.service.ts
- [x] `POST /ai/courses/{id}/voice-tutor` — core: core/services/learning.service.ts
- [x] `POST /ai/lessons/{id}/generate-quiz` — core: core/services/learning.service.ts
- [ ] `GET /ai/lessons/{id}/index-status` — NO UI
- [x] `POST /ai/lessons/{id}/ingest` — core: core/services/learning.service.ts
- [x] `POST /ai/lessons/{id}/mindmap` — core: core/services/learning.service.ts
- [x] `POST /ai/lessons/{id}/summarize` — core: core/services/learning.service.ts
- [ ] `GET /ai/students/{id}/risk` — NO UI
- [ ] `POST /ai/submissions/{id}/grade-essay` — NO UI
- [ ] `GET /ai/submissions/{id}/plagiarism` — NO UI

## AssessmentsController
- [x] `PUT /attempt-answers/{id}/grade` — features: features/authoring/authoring.page.ts (manual grading desk)
- [x] `GET /attempts/{id}` — core: core/services/learning.service.ts
- [x] `PUT /attempts/{id}/answers` — core: core/services/learning.service.ts
- [x] `POST /attempts/{id}/events` — core: core/services/learning.service.ts
- [x] `GET /attempts/{id}/result` — core: core/services/learning.service.ts
- [x] `POST /attempts/{id}/submit` — core: core/services/learning.service.ts
- [x] `GET /courses/{id}/exams` — core: core/services/learning.service.ts
- [x] `POST /courses/{id}/exams` — core: core/services/learning.service.ts
- [x] `GET /courses/{id}/question-banks` — core: core/services/learning.service.ts
- [x] `PUT /exams/{id}` — features: features/authoring/authoring.page.ts (exam settings editor)
- [x] `GET /exams/{id}/attempts` — core: core/services/learning.service.ts
- [x] `POST /exams/{id}/attempts` — core: core/services/learning.service.ts
- [x] `POST /exams/{id}/publish` — core: core/services/learning.service.ts
- [x] `POST /question-banks` — core: core/services/learning.service.ts
- [x] `POST /question-banks/{id}/import` — features: features/authoring/authoring.page.ts (question import)
- [x] `GET /question-banks/{id}/questions` — core: core/services/learning.service.ts
- [x] `POST /question-banks/{id}/questions` — core: core/services/learning.service.ts
- [x] `DELETE /questions/{id}` — core: core/services/learning.service.ts
- [x] `PUT /questions/{id}` — core: core/services/learning.service.ts

## AssignmentsController
- [x] `PUT /assignments/{id}` — core: core/services/assignments.service.ts
- [x] `POST /assignments/{id}/groups` — core: core/services/assignments.service.ts
- [x] `POST /assignments/{id}/peer-reviews/assign` — core: core/services/assignments.service.ts
- [x] `GET /assignments/{id}/submissions` — core: core/services/assignments.service.ts
- [x] `POST /assignments/{id}/submissions` — core: core/services/assignments.service.ts
- [x] `GET /courses/{id}/assignments` — core: core/services/assignments.service.ts
- [x] `POST /courses/{id}/assignments` — core: core/services/assignments.service.ts
- [x] `GET /peer-reviews/mine` — core: core/services/assignments.service.ts
- [x] `PUT /peer-reviews/{id}` — core: core/services/assignments.service.ts
- [x] `GET /submissions/{id}` — core: core/services/assignments.service.ts
- [x] `POST /submissions/{id}/comments` — core: core/services/assignments.service.ts
- [x] `PUT /submissions/{id}/grade` — core: core/services/assignments.service.ts
- [x] `POST /submissions/{id}/return` — core: core/services/assignments.service.ts

## AuthController
- [x] `POST /auth/biometric/enable` — core: core/services/biometric.service.ts
- [x] `POST /auth/forgot-password` — core: core/services/auth.service.ts
- [x] `POST /auth/login` — core: core/services/auth.service.ts
- [x] `POST /auth/logout` — core: core/services/auth.service.ts
- [x] `POST /auth/otp/request` — features: features/auth/register/register.page.ts (resend verification code)
- [ ] `POST /auth/otp/verify` — NO UI (documented: duplicate of /auth/verify-email; phone purpose returns 501 SMS_NOT_CONFIGURED in backend)
- [x] `POST /auth/refresh` — core: core/services/auth.service.ts
- [x] `POST /auth/register` — core: core/services/auth.service.ts
- [x] `POST /auth/reset-password` — core: core/services/auth.service.ts
- [ ] `POST /auth/social/{id}` — NO UI (documented: backend handler is a stub that always returns NotConfigured)
- [x] `POST /auth/verify-email` — core: core/services/auth.service.ts

## CommerceController
- [x] `GET /affiliate/me` — core: core/services/learning.service.ts
- [x] `POST /affiliate/profiles` — core: core/services/learning.service.ts
- [x] `GET /api-keys` — core: core/services/finance.service.ts
- [x] `POST /api-keys` — core: core/services/finance.service.ts
- [x] `DELETE /api-keys/{id}` — core: core/services/finance.service.ts
- [x] `GET /audit-logs` — core: core/services/finance.service.ts
- [x] `GET /bundles` — core: core/services/learning.service.ts
- [x] `POST /bundles` — core: core/services/learning.service.ts
- [x] `GET /cart` — core: core/services/learning.service.ts
- [x] `POST /cart/coupon` — core: core/services/learning.service.ts
- [x] `POST /cart/items` — core: core/services/learning.service.ts
- [x] `DELETE /cart/items/{id}` — core: core/services/learning.service.ts
- [x] `GET /coupons` — core: core/services/learning.service.ts
- [x] `POST /coupons` — core: core/services/learning.service.ts
- [x] `PUT /coupons/{id}` — core: core/services/learning.service.ts
- [x] `PUT /courses/{id}/instructor-share` — features: features/finance/finance.page.ts (share override, confirm strip)
- [x] `PUT /courses/{id}/learning-weight` — features: features/finance/finance.page.ts (learning weight editor)
- [x] `GET /dashboards/admin` — core: core/services/api.service.ts
- [x] `GET /dashboards/instructor` — core: core/services/api.service.ts
- [x] `GET /dashboards/student` — core: core/services/api.service.ts
- [x] `GET /instructor-debits` — core: core/services/finance.service.ts
- [x] `GET /instructor-payouts` — core: core/services/finance.service.ts
- [x] `POST /instructor-payouts/calculate` — core: core/services/finance.service.ts
- [x] `GET /instructor-payouts/statement` — core: core/services/finance.service.ts
- [x] `PUT /instructor-payouts/{id}` — core: core/services/finance.service.ts
- [x] `PUT /instructor-revenue/default` — core: core/services/finance.service.ts
- [x] `GET /instructor-revenue/held` — core: core/services/finance.service.ts
- [x] `PUT /instructor-revenue/held` — core: core/services/finance.service.ts
- [x] `GET /instructor-revenue/shares` — core: core/services/finance.service.ts
- [x] `GET /instructor-revenue/subscription-controls` — features: features/finance/finance.page.ts (controls prefill)
- [x] `PUT /instructor-revenue/subscription-controls` — features: features/finance/finance.page.ts (controls editor, confirm strip)
- [x] `GET /instructor/revenue` — core: core/services/finance.service.ts
- [x] `POST /lessons/{id}/unlock` — core: core/services/learning.service.ts
- [x] `GET /manual-payments` — core: core/services/learning.service.ts
- [x] `POST /manual-payments` — core: core/services/learning.service.ts
- [x] `GET /manual-payments/mine` — core: core/services/learning.service.ts
- [x] `POST /manual-payments/{id}/approve` — core: core/services/learning.service.ts
- [x] `GET /manual-payments/{id}/receipt` — core: core/services/learning.service.ts
- [x] `POST /manual-payments/{id}/reject` — core: core/services/learning.service.ts
- [x] `GET /orders` — core: core/services/learning.service.ts
- [x] `POST /orders/checkout` — core: core/services/learning.service.ts
- [x] `GET /orders/{id}` — features: features/orders/orders.page.ts (order detail expansion)
- [x] `GET /orders/{id}/invoice` — core: core/services/learning.service.ts
- [x] `POST /orders/{id}/refunds` — core: core/services/finance.service.ts
- [ ] `POST /payments/gateways/{id}/webhook` — NO UI
- [x] `GET /recycle-bin` — core: core/services/finance.service.ts
- [x] `POST /recycle-bin/{id}/{id}/restore` — core: core/services/finance.service.ts
- [x] `GET /reports/{id}/export` — core: core/services/api.service.ts
- [x] `GET /subscription-plans` — core: core/services/learning.service.ts
- [x] `POST /subscription-plans` — core: core/services/learning.service.ts
- [x] `POST /subscriptions` — core: core/services/learning.service.ts
- [x] `POST /subscriptions/{id}/cancel` — orders page (My subscriptions, confirm strip): features/orders/orders.page.ts
- [x] `GET /voucher-batches` — core: core/services/learning.service.ts
- [x] `POST /voucher-batches` — core: core/services/learning.service.ts
- [x] `GET /voucher-batches/{id}/export` — core: core/services/learning.service.ts
- [x] `POST /voucher-batches/{id}/void` — core: core/services/learning.service.ts
- [x] `POST /vouchers/redeem` — wallet page (course voucher box): features/wallet/wallet.page.ts
- [x] `GET /wallet` — core: core/services/learning.service.ts
- [x] `POST /wallet/topup/gateway` — core: core/services/learning.service.ts
- [x] `POST /wallet/topup/voucher` — core: core/services/learning.service.ts
- [x] `GET /wallet/transactions` — core: core/services/learning.service.ts
- [x] `GET /webhooks` — features: features/ops/ops.page.ts (webhooks tab)
- [x] `POST /webhooks` — features: features/ops/ops.page.ts (webhook create, one-time secret)
- [x] `POST /webhooks/deliveries/{id}/retry` — features: features/ops/ops.page.ts (retry failed delivery)
- [x] `DELETE /webhooks/{id}` — features: features/ops/ops.page.ts (delete with confirm strip)
- [x] `PUT /webhooks/{id}` — features: features/ops/ops.page.ts (webhook edit)
- [x] `GET /webhooks/{id}/deliveries` — features: features/ops/ops.page.ts (deliveries expand)

## CommunityController
- [x] `GET /announcements` — core: core/services/community.service.ts
- [x] `POST /announcements` — core: core/services/community.service.ts
- [x] `DELETE /bookmarks/{id}` — core: core/services/learning.service.ts
- [x] `GET /chat/rooms` — core: core/services/community.service.ts
- [x] `GET /chat/rooms/{id}/messages` — core: core/services/community.service.ts
- [x] `POST /chat/rooms/{id}/messages` — core: core/services/community.service.ts
- [x] `GET /courses/{id}/live-sessions` — core: core/services/community.service.ts
- [x] `POST /courses/{id}/live-sessions` — core: core/services/community.service.ts
- [x] `GET /courses/{id}/threads` — core: core/services/community.service.ts
- [x] `POST /courses/{id}/threads` — core: core/services/community.service.ts
- [x] `GET /learning-paths` — core: core/services/community.service.ts
- [x] `POST /learning-paths` — core: core/services/community.service.ts
- [x] `GET /learning-paths/manage` — core: core/services/learning.service.ts
- [x] `DELETE /learning-paths/{id}` — core: core/services/community.service.ts
- [x] `GET /learning-paths/{id}` — core: core/services/community.service.ts
- [x] `PUT /learning-paths/{id}` — core: core/services/community.service.ts
- [x] `POST /learning-paths/{id}/enroll` — core: core/services/community.service.ts
- [x] `GET /lessons/{id}/bookmarks` — core: core/services/learning.service.ts
- [x] `POST /lessons/{id}/bookmarks` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}/notes` — core: core/services/learning.service.ts
- [x] `POST /lessons/{id}/notes` — core: core/services/learning.service.ts
- [x] `GET /live-sessions/{id}/attendance` — core: core/services/community.service.ts
- [x] `POST /live-sessions/{id}/join` — core: core/services/community.service.ts
- [x] `DELETE /notes/{id}` — core: core/services/learning.service.ts
- [x] `PUT /notes/{id}` — core: core/services/learning.service.ts
- [x] `GET /notifications` — core: core/services/api.service.ts
- [ ] `POST /notifications/push-token` — NO UI
- [x] `POST /notifications/{id}/read` — core: core/services/api.service.ts
- [x] `POST /posts/{id}/upvote` — core: core/services/community.service.ts
- [x] `POST /study-sessions` — player page (study session recorded on leave): features/player/player.page.ts
- [x] `GET /threads/{id}` — core: core/services/community.service.ts
- [x] `POST /threads/{id}/pin` — core: core/services/community.service.ts
- [x] `POST /threads/{id}/posts` — core: core/services/community.service.ts
- [x] `POST /threads/{id}/upvote` — core: core/services/community.service.ts

## ContentController
- [x] `PUT /courses/{id}/prerequisites` — features: features/builder/builder.page.ts (prerequisite picker, confirm strip)
- [x] `PUT /courses/{id}/pricing` — core: core/services/learning.service.ts
- [x] `PUT /courses/{id}/sections/order` — core: core/services/learning.service.ts
- [x] `GET /courses/{id}/staff` — features: features/builder/builder.page.ts (staff list)
- [x] `PUT /courses/{id}/staff` — features: features/builder/builder.page.ts (staff editor, confirm strip)
- [x] `POST /interactive-quizzes/{id}/answer` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}/chapters` — core: core/services/learning.service.ts
- [x] `PUT /lessons/{id}/chapters` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}/interactive-quizzes` — core: core/services/learning.service.ts
- [x] `PUT /lessons/{id}/interactive-quizzes` — core: core/services/learning.service.ts
- [x] `POST /lessons/{id}/media` — core: core/services/learning.service.ts
- [x] `PUT /lessons/{id}/price` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}/resources` — core: core/services/learning.service.ts
- [x] `POST /lessons/{id}/resources` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}/resources/{id}/download` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}/slides` — core: core/services/learning.service.ts
- [x] `PUT /lessons/{id}/slides` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}/stream` — core: core/services/learning.service.ts
- [x] `PUT /sections/{id}/lessons/order` — core: core/services/learning.service.ts

## EngagementController
- [x] `POST /certificates/issue` — core: core/services/engagement.service.ts
- [x] `GET /certificates/mine` — core: core/services/engagement.service.ts
- [x] `GET /certificates/verify/{id}` — core: core/services/engagement.service.ts
- [x] `GET /certificates/{id}/pdf` — core: core/services/engagement.service.ts
- [x] `GET /gamification/leaderboard` — core: core/services/engagement.service.ts
- [x] `GET /gamification/me` — core: core/services/engagement.service.ts
- [x] `GET /jobs/{id}` — finance page (payout-calculation job polling): features/finance/finance.page.ts
- [x] `GET /parent/alerts` — core: core/services/engagement.service.ts
- [x] `GET /parent/children` — core: core/services/api.service.ts
- [x] `GET /parent/children/{id}/summary` — core: core/services/api.service.ts
- [x] `POST /parent/links` — core: core/services/engagement.service.ts
- [x] `GET /parent/links/pending` — core: core/services/engagement.service.ts
- [x] `PUT /parent/links/{id}` — core: core/services/engagement.service.ts
- [x] `POST /students/me/link-codes` — core: core/services/engagement.service.ts

## HealthController
- [ ] `GET /health` — NO UI

## IntegrationController
- [ ] `GET /integration/enrollments` — NO UI
- [ ] `GET /integration/orders` — NO UI

## LmsController
- [x] `GET /categories` — core: core/services/admin.service.ts
- [x] `POST /categories` — core: core/services/admin.service.ts
- [x] `DELETE /categories/{id}` — core: core/services/admin.service.ts
- [x] `PUT /categories/{id}` — core: core/services/admin.service.ts
- [x] `GET /courses` — core: core/services/learning.service.ts
- [x] `POST /courses` — core: core/services/learning.service.ts
- [x] `GET /courses/mine` — core: core/services/learning.service.ts
- [x] `DELETE /courses/{id}` — core: core/services/learning.service.ts
- [x] `GET /courses/{id}` — core: core/services/learning.service.ts
- [x] `PUT /courses/{id}` — core: core/services/learning.service.ts
- [x] `POST /courses/{id}/approve` — core: core/services/admin.service.ts
- [x] `POST /courses/{id}/enroll` — core: core/services/learning.service.ts
- [x] `GET /courses/{id}/progress` — core: core/services/learning.service.ts
- [x] `POST /courses/{id}/reject` — core: core/services/admin.service.ts
- [x] `POST /courses/{id}/sections` — core: core/services/learning.service.ts
- [x] `POST /courses/{id}/submit-for-approval` — core: core/services/learning.service.ts
- [x] `DELETE /lessons/{id}` — core: core/services/learning.service.ts
- [x] `GET /lessons/{id}` — core: core/services/learning.service.ts
- [x] `PUT /lessons/{id}` — core: core/services/learning.service.ts
- [x] `PUT /lessons/{id}/progress` — core: core/services/learning.service.ts
- [x] `GET /me/enrollments` — core: core/services/learning.service.ts
- [x] `DELETE /sections/{id}` — core: core/services/learning.service.ts
- [x] `PUT /sections/{id}` — core: core/services/learning.service.ts
- [x] `POST /sections/{id}/lessons` — core: core/services/learning.service.ts

## MeController
- [x] `GET /me` — core: core/services/api.service.ts
- [x] `PUT /me` — core: core/services/api.service.ts
- [x] `POST /me/change-password` — profile page: features/profile/profile.page.ts
- [x] `GET /me/devices` — core: core/services/api.service.ts
- [x] `DELETE /me/devices/{id}` — core: core/services/api.service.ts

## TenantsController
- [x] `GET /tenants` — core: core/services/admin.service.ts
- [x] `POST /tenants` — core: core/services/admin.service.ts
- [x] `GET /tenants/current/branding` — core: core/services/tenant.service.ts
- [ ] `GET /tenants/current/logo` — NO UI
- [x] `GET /tenant/feature-flags` — core: core/services/admin.service.ts
- [x] `PUT /tenant/feature-flags/{key}` — core: core/services/admin.service.ts
- [x] `POST /tenant/logo` — core: core/services/admin.service.ts
- [x] `PUT /tenant/settings` — core: core/services/admin.service.ts
- [x] `GET /tenants/{id}` — core: core/services/admin.service.ts
- [x] `PUT /tenants/{id}` — core: core/services/admin.service.ts
- [x] `DELETE /tenants/{id}/support-access` — core: core/services/admin.service.ts
- [x] `POST /tenants/{id}/support-access` — core: core/services/admin.service.ts

## UsersController
- [x] `GET /users` — core: core/services/admin.service.ts
- [x] `POST /users` — core: core/services/admin.service.ts
- [x] `GET /users/{id}` — core: core/services/admin.service.ts
- [x] `PUT /users/{id}` — core: core/services/admin.service.ts
- [x] `POST /users/{id}/deactivate` — core: core/services/admin.service.ts
- [x] `GET /users/{id}/devices` — core: core/services/admin.service.ts
- [x] `POST /users/{id}/devices/reset` — core: core/services/admin.service.ts
- [x] `PUT /users/{id}/roles` — core: core/services/admin.service.ts

## Classification of the 13 endpoints with no UI

### External-system or machine-only (no human workflow belongs in the app)
- `GET /health` — uptime probe for infrastructure
- `POST /payments/gateways/{id}/webhook` — server-to-server payment gateway callbacks
- `GET /integration/enrollments`, `GET /integration/orders` — server-to-server integration API (API-key auth)
- `GET /tenants/current/logo` — public logo asset, consumed via <img>/CSS, not an API call

### AI-service-dependent (require the Python AI service; no UI built)
- `POST /ai/assignments/{id}/plagiarism-check`, `GET /ai/submissions/{id}/plagiarism`
- `POST /ai/submissions/{id}/grade-essay`
- `GET /ai/lessons/{id}/index-status`, `GET /ai/students/{id}/risk`

### Native-app-only
- `POST /notifications/push-token` — device push registration, belongs to a native shell

### Documented exclusions (backend cannot support a human workflow today)
- `POST /auth/social/{id}` — backend handler is a stub that always returns NotConfigured; no working flow exists to build UI against
- `POST /auth/otp/verify` — duplicate of `/auth/verify-email` for email verification (which has UI); the phone-verification purpose returns 501 SMS_NOT_CONFIGURED because no SMS provider is configured in the backend

All other previously listed gaps (exam edit, manual grading, question import, instructor share, learning weight, subscription controls, course staff, prerequisites, order detail, webhooks, OTP resend) are now implemented and verified by tools/verify-round5.mjs (44/44 checks, role-specific sessions).
