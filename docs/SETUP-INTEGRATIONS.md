# External integrations setup

Every integration below is **config-driven and off by default**: with no configuration the app runs fully
(nothing crashes, nothing half-works — the feature simply stays disabled). No secrets belong in the
repository; use environment variables (`Section__Key=value`) or user-secrets for every value here.

## Database migrations

Schema changes ship as EF Core migrations under `backend/src/SmartLMS.Infrastructure/Persistence/Migrations/`
(the generator emits them; `docs/ef-migration.sql` is the idempotent script of the whole model).

- Apply with the CLI: `dotnet ef database update --project src/SmartLMS.Infrastructure --startup-project src/SmartLMS.API`
- Or apply `docs/ef-migration.sql` to the database manually (idempotent, safe to re-run).
- This branch adds one migration on top of the previous batch: `LessonTranscripts` (timed caption cues).

## Google social login (web)

Config section `Authentication:Google`:

- `Authentication:Google:Enabled` = `true`
- `Authentication:Google:ClientId` = the **Web application** OAuth client ID from Google Cloud Console
  (APIs & Services → Credentials → OAuth 2.0 Client IDs → type "Web application"). Add the app's origin to
  "Authorized JavaScript origins". The ID token the browser gets from Google Sign-In is verified server-side
  against this client ID (`/auth/social/google`).

Limitations: only Google is implemented (no Apple). The native (iOS/Android) Google sign-in flow is NOT
wired in this build — the button is web-only; native would need the Capacitor Google Auth plugin and the
native client IDs, and is untested here. A Google account auto-links per academy (tenant) to the local
account with the same verified email; TenantAdmin/SuperAdmin accounts are never auto-linked.

## Push notifications

Two halves, both required per platform. Supported platforms: **Android (native)** and **Web**.
**iOS is NOT supported in this build** (see below).

### Server side (FCM HTTP v1) — required for every platform

1. Create a Firebase project (console.firebase.google.com).
2. Project settings → Service accounts → Generate new private key → keep the JSON **outside the repo**.
3. Configure:
   - `Push:Fcm:Enabled` = `true`
   - `Push:Fcm:ServiceAccountPath` = absolute path to that JSON.

### Web push

1. Same Firebase project → Project settings → General → add a **Web app**, copy its client config
   (`apiKey`, `authDomain`, `projectId`, `messagingSenderId`, `appId` — all public-by-design client values).
2. Project settings → Cloud Messaging → Web Push certificates → generate a key pair; copy the **public** key
   (VAPID key). The private half stays at Google.
3. Configure:
   - `Push:Web:Enabled` = `true`
   - `Push:Web:VapidPublicKey` = the VAPID public key
   - `Push:Web:Firebase:ApiKey` / `:AuthDomain` / `:ProjectId` / `:MessagingSenderId` / `:AppId`
4. `GET /notifications/push-config` (authenticated) serves `{ enabled, vapidPublicKey, firebase }`. If any
   required value is missing the endpoint returns `enabled: false` and the app skips web registration
   entirely — the in-app notifications page remains the channel.
5. The app registers `public/firebase-messaging-sw.js` (config passed via its query string), asks the user
   for notification permission, gets an FCM web token and posts it to `/notifications/push-token`
   (`platform: "Web"`). Logout deletes exactly this device's token.

### Android (native)

- Put `google-services.json` (from the Firebase project, Android app with the app's package id) into the
  Capacitor Android project, then build. The app registers with FCM via `@capacitor/push-notifications`
  and posts the FCM token (`platform: "Android"`).

### iOS (native) — unsupported in this build

On iOS `@capacitor/push-notifications` returns an **APNs device token**, which the FCM sender cannot use,
so the app explicitly skips registration on iOS rather than storing an undeliverable token. Supporting iOS
means embedding the Firebase iOS SDK in the app (GoogleService-Info.plist) and sourcing an **FCM** token
(e.g. via `@capacitor-community/fcm`), plus APNs key configuration in the Firebase project. Untested here.

## Transactional email (SMTP)

Config section `Email` (off unless complete):

- `Email:Enabled` = `true`
- `Email:FromAddress` (and `Email:FromName`) — required
- `Email:Host`, `Email:Port` (default 587), `Email:UseSsl` (default true), `Email:UserName`,
  `Email:Password` (env var `Email__Password` only)
- Alternative for a local pickup folder instead of a server: `Email:PickupDirectory` (IIS-style drop dir;
  used by `GET /auth/mail-delivery` = `local-outbox` in development).

Until `Enabled` + `FromAddress` + (`Host` or `PickupDirectory`) are set, the disabled transport sends
nothing. Covers OTP, welcome, enrollment, receipts, completion certificates, approval results and
live-session reminders.

## Whisper transcription (AI service)

The ai-service transcribes uploaded video/audio with faster-whisper and returns timed cues
(`ai-service/app/config.py`):

- `WHISPER_MODEL` (default `small` — `tiny`/`base` are faster and weaker, `medium`/`large-v3` need much more RAM/VRAM)
- `WHISPER_DEVICE` (`cpu` default; `cuda` with a GPU)
- `WHISPER_COMPUTE_TYPE` (`int8` default on CPU; `float16` on GPU)

The model downloads on first use to the faster-whisper cache. The .NET side stores cues in
`LessonTranscripts` when the ingest job runs (re-ingesting a lesson replaces its cues wholesale) and serves
them at `GET /lessons/{id}/transcript` to enrolled students; the player renders captions + a seekable
transcript.
