# SmartLMS frontend - coverage notes (rounds 1-5)

## Test status (read first)
- All UI verification ran against contract fixtures in headless Chrome (harnesses in tools/verify-*.mjs), not a live backend. Fixtures mirror the backend controllers' actual response shapes.
- SuperAdmin sign-in was verified only as: the platform login call omits the X-Tenant-Id header (SuperAdmin identity is `platform:<email>`, tenant id null) and the full login -> tenants -> support-access flow passes against fixtures. A seeded SuperAdmin login against a live backend was NOT run (no live backend here).
- Backend additions (smartlms-backend-update-round2.zip) were review-only source when delivered. SUPERSEDED 2026-11-25: compiled and tested; see README-VERIFICATION.txt (dotnet build 0 errors, 143/143 tests pass). Still build and test in your own environment before deploy; install/backup steps in README-INSTALL.txt.
- No real payments, deployments, or destructive actions were performed. Gateways are unconfigured; the only testable checkout path is manual payments (receipt upload -> admin approve/reject).

## Completed interfaces
- Round 1: platform login boundary (header-omission + fixture flow above), register/forgot password, tenants (SuperAdmin), users, course approvals with content preview, academy settings, destructive actions behind two-tap confirms.
- Assignments: staff create/list/grade desk with submission content; student homework list, submit (text and/or an external HTTPS link - the backend SubmitRequest carries TextContent and LinkUrl only; there is no file upload anywhere in the contract), view grade, peer review.
- Community: threads, announcements; chat rooms (chronology, Load older, dedupe on send); live sessions (schedule, popup-safe join with explicit Open-session fallback, attendance).
- Learning paths: /app/paths student list + enroll; staff manage (create/edit/delete, drafts via new backend endpoint with old-backend notice fallback); IsMandatory preserved by backend fix.
- Achievements: XP/level/streak/badges, leaderboard, certificates (list, verify box, authenticated PDF download), parent link-code generation; manager certificate issuing with resolved student picker (search by name/email, course picker, POST /certificates/issue, verification code shown). Harness covers BOTH issuer roles the backend authorizes (TenantAdmin and Instructor - UsersController Viewers const), each in its own session.
- Parent area: link-by-code, pending/active states, child summaries (study hours, exam average, per-course progress, risk tags), alerts feed.
- Approvals: pending parent-link requests (new backend endpoint, update-required notice on older backends).
- Player: notes incl. inline edit, bookmarks, chapters, resources; study session recording on leaving a lesson (POST /study-sessions, students only, >= 30 s, capped at 4 h, fire-and-forget, skipped offline).
- Finance (/app/finance, TenantAdmin + Instructor): admin dashboard KPIs/queues, revenue shares (default + per-course overrides), payout statement, held revenue with labeled percent input and full-context confirm strip, debits, payouts (calculate job with live progress polling via GET /jobs/{id} every 5 s until it settles, then payouts refetch), refund recording. Instructor sees own revenue only; no admin endpoints called.
- Ops (/app/ops, TenantAdmin + SuperAdmin): audit log (entity/action/date filters), recycle bin (entity filter, confirm-strip restore), API keys (create shows one-time key with store-now warning, confirm-strip revoke).
- Profile: change password (POST /me/change-password) alongside the existing profile edit.
- Wallet: course-voucher redemption (POST /vouchers/redeem) in its own box, separate from wallet top-up; redeemed course title shown.
- Orders: "My subscriptions" section (GET /subscriptions/mine - new backend endpoint in the update package) with per-plan status/period and a cancel confirm strip that names plan, price and consequence; list refetches after cancel.
- Old-backend compatibility: missing new endpoints (404) show a concise update-required notice with graceful fallback; 401/403/5xx/network surface as real errors. Tested both modes, including executable old-backend runs for the round-5 additions: when GET prerequisites, GET learning-weight or GET /exams/{id} 404, the replacing save controls are not rendered and zero write requests fire (asserted in verify-round5).

## Verification
- npm test: 73/73. i18n EN/AR parity scan: clean. Builds green.
- Harnesses: verify-round1 10/10, verify-round2 15/15, verify-homework 13/13, verify-community 9/9, verify-chat-live 19/19, verify-oldbackend 6/6, verify-achievements 22/22 (incl. certificate issuing as TenantAdmin and as Instructor), verify-finance 48/48, verify-gaps 12/12 (change-password, course voucher, subscriptions + cancel, job polling, study-session recording), verify-round5 56/56 (role-specific sessions below).
- Confirmation strips show entity, amount/share and consequence before any write is sent; success messages clear on tab change; lists refetch after writes (asserted).
- Route-prefix correction (round 5): the round-4 audit initially mis-prefixed the four TenantSettingsController routes as /tenants/... (plural), because TenantsController.cs holds two controller classes with different route prefixes and the audit walk did not track prefixes per class. A systematic fixture-vs-backend check flagged the discrepancy; the actual backend routes are the SINGULAR /tenant/settings, /tenant/logo, /tenant/feature-flags and /tenant/feature-flags/{key} (TenantSettingsController), while /tenants/... (plural) is TenantsController (branding, CRUD, support-access). The delivered frontend had the correct singular paths all along; service, spec and fixture were reverted to them and re-verified against the controller source.

## Round 5: remaining human-workflow gaps closed
- Assessments (Instructor): exam settings editor prefilled from the new GET /exams/{id} (settings-only PUT; exam kind immutable); manual grading desk (attempts list with resolved student names, per-answer points + feedback, view refetched after save); question-bank import (JSON/CSV paste).
- Builder (Instructor): course staff management (list, user search to add, remove, confirm strip shows the resulting list before the PUT replaces it); course prerequisites (prefilled from the new GET, checkbox picker, confirm strip states the consequence).
- Finance (TenantAdmin): per-course instructor share override (confirm strip shows course, instructor, percent); subscription allocation controls (prefilled, confirm strip); course learning weight (current value shown via the new GET, minutes converted to seconds, confirm strip).
- Ops (TenantAdmin/SuperAdmin): webhooks tab (list, create with event-type picker and one-time secret display + store-now warning, edit, delete with confirm strip, deliveries expand, retry failed).
- Orders (Student/Instructor): order detail expansion (items, totals, payments, invoice number).
- Register: "Resend the code" on the verification step (POST /auth/otp/request, purpose EmailVerification).
- Backend update package grew to 12 files: new GET /exams/{id}, GET /courses/{id}/prerequisites and GET /courses/{courseId}/learning-weight endpoints so no screen guesses persisted state.
- verify-round5.mjs proves each flow end to end with role-specific sessions: request payloads asserted, confirm strips asserted to show entity/amount/consequence before the write fires, post-write refetches asserted, one-time webhook secret display asserted, no horizontal overflow on every new screen. Grading answers render recognized shapes ({text} as readable text; {optionId}/{optionIds}/{pairs} as labeled summaries) with a clearly labeled raw fallback for unknown shapes. Regression assertions pin the dynamic-name NgModel fix: option text vs correct-checkbox independence (question POST body) and grading points vs feedback independence.
- Documented exclusions (no UI by design, verified against backend source): POST /auth/social/{id} is a backend stub that always returns NotConfigured; POST /auth/otp/verify duplicates /auth/verify-email and its phone purpose returns 501 SMS_NOT_CONFIGURED (no SMS provider configured).

## Endpoint-to-UI audit
See ENDPOINT-COVERAGE.md: all 242 backend controller endpoints walked and matched against the frontend source. 229 covered, 13 without UI: 5 external-system/machine-only, 5 AI-service-dependent, 1 native-app-only (push token), and 2 documented exclusions (social stub, otp/verify duplicate). The frontend also covers the endpoints added by the backend update package: GET /subscriptions/mine, GET /exams/{id}, GET /courses/{id}/prerequisites, GET /courses/{courseId}/learning-weight.

## Known limits / remaining gaps
- 13 endpoints have no UI (full classification in ENDPOINT-COVERAGE.md): 5 external-system/machine-only, 5 AI-service-dependent, 1 native-app-only, 2 documented backend exclusions.
- Backend additions: compiled and tested on 2026-11-25 (README-VERIFICATION.txt: build 0 errors, 143/143 tests pass on SQLite test host). Live round-trip against your SQL Server is still owed by whoever deploys.
- Certificate PDF generation requires Certificates:VerificationBaseUrl configured on the backend (https outside Development), else the endpoint 500s by design.
- Real payment gateways are unconfigured in the delivered environment, so gateway checkout is untested by design.
- AI features need the Python AI service running; not covered.
- SignalR chat/live hubs were verified through their REST contracts; realtime delivery itself not exercised here.
- Held-revenue, statement, dashboard and instructor-earnings amounts carry no currency field from the API; the UI says so explicitly instead of assuming one.
