# Phase 7: learning player, monetization and AI screens (Ionic Angular)

Built on the approved Phase 6 design. The app talks only to the .NET API. The 8-phase plan stops here: Phase 8 (offline sync, security audit, deployment) is not started.

## Screens (all under `/app`, lazy loaded, separate .ts/.html/.scss files)
| Route | Screen | API used |
|---|---|---|
| `courses` | Catalog with search | GET /courses?q= |
| `courses/:id` | Course detail, outline, enroll or add to cart, exams list | GET /courses/{id}, /me/enrollments, POST /courses/{id}/enroll, /cart/items, GET /courses/{id}/exams |
| `paths` | Learning paths, enroll | GET /learning-paths, /learning-paths/{id}, POST .../enroll |
| `learn/:courseId/:lessonId` | Lesson player (video, audio, PDF, article, link), progress, interactive quiz pauses, AI tutor drawer | GET /lessons/{id}, /lessons/{id}/stream, PUT /lessons/{id}/progress, GET /lessons/{id}/interactive-quizzes, POST /interactive-quizzes/{id}/answer |
| `exam/:examId` | Exam runner: all question types, timer, autosave, tab-switch events, submit, result | POST /exams/{id}/attempts, GET/PUT /attempts/{id}..., POST /attempts/{id}/events, /submit |
| `wallet` (Student) | Balance, history, voucher top-up, manual payment receipt for a wallet top-up | GET /wallet, /wallet/transactions, POST /wallet/topup/voucher, /manual-payments, GET /manual-payments/mine |
| `cart` (Student) | Cart, coupon preview, checkout with wallet or voucher | GET /cart, POST /cart/coupon, /orders/checkout |
| `ai` (Student) | Voice tutor (microphone), study planner | POST /ai/courses/{id}/voice-tutor, /study-plan |
| `build` (Instructor) | Course builder: course, section, lesson, media upload, submit for approval, AI index, AI quiz generator | /courses, /sections, /lessons, /ai/lessons/{id}/ingest, /generate-quiz |

AI tutor drawer (inside the player): tutor chat with citations that seek the video, semantic video search, lesson summary, and an SVG mind map viewer.

## Behaviour worth knowing
- Video is fetched from the protected stream endpoint with the bearer token and played from a blob URL. This holds the whole file in memory, so very large videos need range streaming or a signed URL (not built).
- Dynamic watermark (name, email, time, moves every 7 s) and a privacy screen (picture hidden and playback paused when the window or tab loses focus). These are web-page deterrents. A web page cannot block OS screen capture or recording. Native screen-capture blocking needs a Capacitor plugin and is not built.
- AI replies show "based on course material" or "not found in course material" from the API's `grounded` flag, show citations, and carry a notice that AI can be wrong. AI quiz output is shown as a draft; nothing is saved to the question bank.
- The exam clock shown is a display only; the server owns the real expiry.
- Menu items exist only for built screens, filtered by role. The API still enforces roles.

## Not built or not wired (honest list)
- Online gateway top-up and gateway checkout UI. Manual payment receipt for a specific order (purpose "Order") is not wired; only wallet top-up receipts are.
- Checkout response is shown only as a generic success message, because the API returns an untyped object.
- SignalR live updates (AI processing status, chat) have no client.
- Instructor builder: no drag and drop reordering, no edit or delete of existing sections or lessons, no question bank or exam editor, no chapter, slide or interactive-quiz point editor, no resource upload.
- Voice tutor needs a browser that supports MediaRecorder and a microphone permission. Untested on real devices.
- Admin, parent and TeachingAssistant roles have no Phase 7 screens other than the catalog view.
- No dark mode, system fonts only. Arabic checked on mobile for catalog and cart only.

## Test evidence
- `ng build`: no warnings. `ng test`: unit tests for the service contract (paths, bodies, blob, multipart), exam answer shapes, mind map layout, plus all Phase 6 tests.
- Screenshots come from `frontend/tools/screenshots.mjs`, which serves the built app and answers API calls from fixtures shaped from the backend code. They are not from a running .NET backend, SQL Server or AI service. No end to end run against the real stack has been done.
- The mock lesson video is a generated test pattern (`frontend/tools/mock-lesson.webm`).


## Revision 2 (reconciliation against the original Phase 7 list)

Added after review:
- Defect fixed: the wallet manual payment was sent as JSON, but `POST /manual-payments` takes multipart with a receipt file (PNG, JPEG or PDF, 6 MB). It now sends multipart and the form requires a file. Tests check the multipart fields.
- Checkout outcome is read by shape: Paid, gateway (checkoutUrl, shown as a link; only http or https links are allowed), manual (AwaitingManualReview, shows the exact total and a receipt form for that order), or other. A failed checkout shows an error line and leaves the cart as the server left it (the API message also comes through the global toast).
- Cart methods now: wallet, voucher, online gateway (Paymob, Fawry, Stripe) and manual transfer.
- Wallet: gateway top-up (opens the checkout link; the server credits the wallet when the gateway webhook arrives) and receipt upload.
- Admin Payments screen (TenantAdmin): manual payment review queue with receipt view, approve and reject with reason; voucher batch generation, list with unused, redeemed and voided counts, CSV download and void.
- Builder: rename and delete sections, edit and delete lessons, move sections and lessons up and down, chapter markers editor, resource file upload. Delete asks twice.
- Tests: 42 passing (service routes and bodies, outcome reading, safe links, reorder).

Still not built (honest list):
- Interactive quiz point editor, question bank and exam editor, slide editor (these need question-bank screens that are not built).
- Student timestamped notes and bookmarks in the player, and downloading lesson resources in the player.
- Order list, invoice download, and affiliate or referral screens.
- Which gateways are enabled is not exposed by the API, so a disabled gateway shows the server error after the click.
- Gateway pages, webhooks, and real payments were not exercised. Nothing here was run against the real .NET API.
- SignalR client, range streaming for video, Arabic RTL checked only on catalog, cart and the admin Payments screen on mobile.

## Revision 3 (full reconciliation)

Added since revision 2:
- Player: chapters (seek), downloadable resources, timestamped notes, bookmarks, interactive slides viewer, pay-per-lesson unlock button on locked lessons. The Capacitor Privacy Screen is enabled on the player and exam screens (native only, ref-counted).
- Orders page (`/app/orders`): order history, invoice PDF download, affiliate panel (code, link, commissions, referrals).
- Catalog: bundles and subscription plans, added to the cart. Cart has an optional referral code sent as `referralCode`.
- Instructor Assessments (`/app/assessments`): question banks, all seven question types, exams drawn from a bank, publish, interactive video quiz points.
- Builder: course pricing (Free, OneTime, PayPerLesson, SubscriptionOnly, price, currency, access days), per-lesson price, `InteractiveSlides` lesson type and a slides editor.
- Admin Pricing and offers (`/app/monetization`): coupons (create, activate, deactivate), bundles, subscription plans, affiliate profiles.
- Backend regression after the API changes: all 134 tests pass, run as split filters (Content 5, Lms 11, Commerce 29, Ai/Api/Assignment 40, the rest 49).
- Wallet page links to Cart and Orders so they are reachable from the mobile tab bar.

Backend additions (real gaps found while reconciling, with tests in `ContentTests`):
- `GET /lessons/{id}/chapters`, `/slides`, `/resources` (same access rule as opening the lesson).
- `PUT /courses/{id}/pricing` and `PUT /lessons/{id}/price` (owner only).

Still not done or not verified:
- Nothing ran end to end against the real .NET, SQL Server or AI stack. Screenshots use a mock API; backend tests are in-process integration tests.
- Privacy Screen plugin untested on a device. Watermark and blur are web-level only.
- The login-only app has no Register screen, so the `?ref=` link in the affiliate panel has no landing page. Referral codes work by typing them in the cart.
- The API has no user list, so the admin affiliate form needs a pasted user ID.
- The API does not say which gateways are enabled. Gateway pages and webhooks were not exercised.
- Whole-file blob video, no range streaming. No SignalR client. Voice tutor untested on a device.
- Orders is reachable on mobile from the Wallet page (students) and the Builder header (instructors).
- Arabic RTL checked on the screens in screenshots 18, 19, 25, 32, 33 only.
