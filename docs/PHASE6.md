# Phase 6: Ionic Angular frontend (shell, auth, white-label, dashboards)

Approved by the user as relayed by Main (Telegram, Nov 24 22:37 Cairo). Phase 7 is not started and not approved.

## Built
- Standalone Angular + Ionic app, every component in separate `.ts` / `.html` / `.scss` files.
- Adaptive shell: `ion-split-pane` with a sidebar (`ion-menu`) at >= 992px, `ion-tabs` bottom bar below it. Only built screens are listed (Dashboard, Notifications, Profile).
- Auth: login page, session in Capacitor Preferences, `authGuard` / `guestGuard` / `roleGuard`, biometric lock page (native only).
- Interceptors: JWT + `X-Tenant-Id` + `X-Device-Id`; one single-flight refresh on 401 with retry, local sign-out if refresh fails; global error toast.
- White-label: `GET /tenants/current/branding` -> validated hex colours -> Ionic CSS variables (shade, tint, contrast). Non-hex values are rejected, so branding cannot inject CSS. Logo and name shown in login and sidebar.
- i18n: English LTR and Arabic RTL, switch sets `<html lang dir>`; layout uses logical CSS properties. Test checks both dictionaries have identical keys.
- Dashboards (data from `/dashboards/*` and `/parent/*`): Student (progress, deadlines, XP/streak, skill radar), Instructor (enrollment trend, lesson drop-off heatmap, quiz difficulty, gross sales), Admin (KPIs, approval queues, revenue by day, CSV/Excel export via `/reports/{kind}/export`), Parent (children, summary, alerts). Charts are plain SVG/CSS, no chart library.
- Profile (roles, language, devices list/remove, biometric toggle), Notifications (list, mark read).
- The app calls only the .NET API. It never calls the AI service.

## Contract reconciliation (against backend handlers, Phase 6 review)
- Checked by reading code: login/refresh/logout bodies (LoginRequest/RefreshRequest/LogoutRequest), TokenResponse, branding (BrandingDto), dashboard routes, `/reports/{kind}/export` (returns a file, kinds orders/enrollments/revenue for admins), DeviceBinding middleware (X-Device-Id required on authenticated /api calls).
- Mismatch found and fixed: `/dashboards/instructor` allows only Instructor and `/dashboards/admin` only TenantAdmin. The first version mapped TeachingAssistant and SuperAdmin to those dashboards, which would 403. They now get the "no dashboard for your role" page. (TA/SuperAdmin dashboards are not built.)
- Mismatch found and fixed: `/auth/logout` needs the bearer token but the app cleared the session before calling it, so the server never revoked the refresh token. It now revokes first, then clears (tested).
- Biometric: the OS prompt gates the app, not the server. With biometric on (native), a cold start keeps the stored session locked: no bearer token is sent and routes go to /lock until the OS confirms. The server only stores a flag (`BiometricLoginEnabled`); it does not verify a biometric.
- Storage limit: session tokens and device id are in Capacitor Preferences (localStorage on web, UserDefaults/SharedPreferences on device), NOT encrypted secure storage. Anyone with device or browser-profile access, or an XSS bug, could read the refresh token. A Keychain/Keystore plugin is the fix and was not added or tested. Refresh tokens rotate and are bound to the device fingerprint, which limits replay from another device. No re-lock on app resume/timeout.
- Export errors: failed exports return a blob, so the toast shows the generic message rather than the server's text.

## Not built / limits
- Biometric, PrivacyScreen, Push, Device plugins are installed and Device/Biometric are wired, but nothing was run on a real Android/iOS device. In a browser biometric reports "not available". PrivacyScreen and push registration are not wired to any screen yet.
- No live SignalR client (no notification hub connection); the notification list is fetched on open.
- Register, forgot/reset password screens, course catalog, player, wallet, AI screens: not built (Phase 7).
- Admin export buttons were exercised only against a mock; the real endpoint's file response was not run from the browser.
- Screenshots come from a mock API (fixtures shaped from the backend code), not from the real backend.
- Not tested on a browser matrix, Windows, or against SQL Server from the UI.

## Run
```
cd frontend
npm install
npm start          # http://localhost:4200 (set apiBaseUrl in src/environments/environment.ts)
npm test           # vitest, 17 tests
npx ng build
```
API CORS allows http://localhost:4200 and http://localhost:8100. The dev API uses a self-signed cert at https://localhost:7200: open it once in the browser and accept it.
Screenshots: `npm i --no-save puppeteer-core`, `npx ng build`, `CHROME=/path/to/chrome node tools/screenshots.mjs shots`.

## Design revision (as relayed by Main, user approved the sampled direction)

- Visual system: system font stack, light page background, white rounded cards, icon-chip KPI cards, hero banner per dashboard, and a gradient from the tenant primary colour to `--brand-end`.
- Tenant contrast: `gradientEnd(primary, accent)` uses the accent only when its readable text colour matches the primary's; otherwise it uses a shade of the primary. Covered by a unit test.
- Applied to login, all four dashboards (student, instructor, admin, parent), notifications, profile, sidebar and tab bar. Arabic RTL checked on login and admin mobile.
- Limits: no dark mode, no web font, screenshots come from a mock API fixture and only desktop and mobile Chrome were rendered. The earlier Phase 6 ZIP is superseded. All earlier Phase 6 limits still apply (tokens in Preferences, biometric untested on a device, no SignalR client, TA and SuperAdmin have no dashboard).
