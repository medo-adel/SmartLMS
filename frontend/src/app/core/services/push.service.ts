import { Injectable, InjectionToken, effect, inject } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { PushNotifications } from '@capacitor/push-notifications';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { ToastController } from '@ionic/angular';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { AuthService, LOGOUT_HOOKS } from './auth.service';

export interface PushFirebaseConfig { apiKey: string; authDomain?: string | null; projectId: string; messagingSenderId: string; appId: string; }
export interface PushConfig { enabled: boolean; vapidPublicKey: string | null; firebase: PushFirebaseConfig | null; }

/** The browser side of web push, isolated behind an interface so the lifecycle is unit-testable without Firebase. */
export interface WebPushDriver {
  /** Loads Firebase, asks notification permission and returns the FCM web token (null when denied/unreachable). */
  getToken(swReg: ServiceWorkerRegistration, vapidKey: string, cfg: PushFirebaseConfig): Promise<string | null>;
  /** Invalidates the current token with FCM (logout). */
  deleteToken(): Promise<void>;
  /** Foreground messages (background ones are shown by the service worker). */
  onForeground(cb: (title?: string, body?: string) => void): void;
}

const FIREBASE_VERSION = '10.14.1';

/** Real driver: loads the Firebase compat bundles from the CDN on demand and talks to FCM. */
@Injectable({ providedIn: 'root' })
export class FirebaseWebPushDriver implements WebPushDriver {
  private messaging: { deleteToken(): Promise<boolean> } | null = null;
  private fg: ((title?: string, body?: string) => void) | null = null;

  private load(src: string): Promise<void> {
    return new Promise((resolve, reject) => {
      const el = document.createElement('script');
      el.src = src; el.onload = () => resolve(); el.onerror = () => reject(new Error('script failed: ' + src));
      document.head.appendChild(el);
    });
  }

  async getToken(swReg: ServiceWorkerRegistration, vapidKey: string, cfg: PushFirebaseConfig): Promise<string | null> {
    try {
      await this.load(`https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-app-compat.js`);
      await this.load(`https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-messaging-compat.js`);
      const fb = (globalThis as { firebase?: { apps: unknown[]; initializeApp(c: object): void; messaging(): { getToken(o: object): Promise<string>; onMessage(cb: (p: { notification?: { title?: string; body?: string } }) => void): void; deleteToken(): Promise<boolean> } } }).firebase;
      if (!fb) return null;
      if (!fb.apps.length) fb.initializeApp(cfg);
      const msg = fb.messaging();
      this.messaging = msg;
      msg.onMessage((p) => this.fg?.(p?.notification?.title, p?.notification?.body));
      return (await msg.getToken({ vapidKey, serviceWorkerRegistration: swReg })) || null;
    } catch { return null; } // permission denied, CDN blocked or FCM unreachable: in-app notifications still work
  }

  async deleteToken(): Promise<void> { try { await this.messaging?.deleteToken(); } catch { /* best effort */ } }
  onForeground(cb: (title?: string, body?: string) => void): void { this.fg = cb; }
}

export const WEB_PUSH_DRIVER = new InjectionToken<WebPushDriver>('WEB_PUSH_DRIVER', { providedIn: 'root', factory: () => new FirebaseWebPushDriver() });

/**
 * Push notifications.
 * Supported platforms:
 *  - Android (native): @capacitor/push-notifications returns an FCM token once google-services.json is in the app.
 *  - Web: FCM web push via a service worker + VAPID key, enabled only when the server serves a complete
 *    Push:Web config from GET /notifications/push-config; otherwise registration is skipped entirely and the
 *    in-app notifications page remains the channel.
 *  - iOS (native): NOT supported by this build. The Capacitor plugin returns an APNs device token there, which
 *    the FCM sender cannot use; sending to iOS needs the Firebase iOS SDK in the app (GoogleService-Info.plist +
 *    an FCM token source such as @capacitor-community/fcm). Skipped explicitly rather than registering a token
 *    the backend can never deliver to. See docs/SETUP-INTEGRATIONS.md.
 * Login registers the device; logout removes exactly this device's token (DELETE /notifications/push-token)
 * before the session is revoked.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastController);
  private readonly auth = inject(AuthService);
  private readonly webDriver = inject(WEB_PUSH_DRIVER);
  private started = false;
  private lastToken: string | null = null; // the token this device registered, for logout cleanup

  constructor() {
    // Registers once per app run, as soon as a session exists (cold start or a later login).
    effect(() => { if (this.auth.isAuthenticated()) void this.init(); });
    // Logout cleanup runs as a logout hook so the DELETE still carries the (about to be revoked) session.
    inject(LOGOUT_HOOKS).push(async () => { await this.teardown(); });
  }

  async init(): Promise<void> {
    if (this.started) return;
    this.started = true;
    if (!Capacitor.isNativePlatform()) { await this.initWeb(); return; }
    if (Capacitor.getPlatform() === 'ios') return; // iOS unsupported in this build (see class docs)
    try {
      let perm = await PushNotifications.checkPermissions();
      if (perm.receive === 'prompt') perm = await PushNotifications.requestPermissions();
      if (perm.receive !== 'granted') return; // user said no: in-app notifications still work
      await PushNotifications.addListener('registration', (t) => void this.registerToken(t.value, 'Android'));
      await PushNotifications.addListener('registrationError', () => { /* FCM not configured on this install: stay silent, in-app still works */ });
      await PushNotifications.addListener('pushNotificationReceived', (n) => void this.showArrival(n.title, n.body));
      await PushNotifications.addListener('pushNotificationActionPerformed', () => void this.router.navigateByUrl('/app/notifications'));
      await PushNotifications.register();
    } catch { /* push unavailable on this device/build: never block the app over it */ }
  }

  private async initWeb(): Promise<void> {
    try {
      if (!('serviceWorker' in navigator) || !('PushManager' in window)) return; // very old browser: in-app only
      const cfg = await firstValueFrom(this.http.get<ApiResponse<PushConfig>>(`${environment.apiBaseUrl}/notifications/push-config`)).then((r) => r.data).catch(() => null);
      if (!cfg?.enabled || !cfg.vapidPublicKey || !cfg.firebase) return; // server has no complete web-push config
      const f = cfg.firebase;
      const q = new URLSearchParams({ apiKey: f.apiKey, projectId: f.projectId, messagingSenderId: f.messagingSenderId, appId: f.appId, ...(f.authDomain ? { authDomain: f.authDomain } : {}) });
      const reg = await navigator.serviceWorker.register(`/firebase-messaging-sw.js?${q}`);
      const token = await this.webDriver.getToken(reg, cfg.vapidPublicKey, f);
      if (!token) return; // permission denied or FCM unreachable: in-app notifications still work
      this.webDriver.onForeground((t, b) => void this.showArrival(t, b));
      await this.registerToken(token, 'Web');
    } catch { /* web push unavailable: never block the app over it */ }
  }

  private async registerToken(token: string, platform: 'Android' | 'Web'): Promise<void> {
    if (!token) return;
    try {
      await firstValueFrom(this.http.post<ApiResponse<object | null>>(`${environment.apiBaseUrl}/notifications/push-token`, { fcmToken: token, platform }));
      this.lastToken = token;
    } catch { /* offline: the next app start re-registers */ }
  }

  /** Logout: remove exactly this device's token while the session is still valid, then invalidate it client-side. */
  private async teardown(): Promise<void> {
    const token = this.lastToken; this.lastToken = null;
    if (token) {
      try { await firstValueFrom(this.http.delete<ApiResponse<object | null>>(`${environment.apiBaseUrl}/notifications/push-token`, { body: { fcmToken: token } })); } catch { /* server keeps a stale row until FCM prunes it as unregistered */ }
    }
    await this.webDriver.deleteToken();
  }

  private async showArrival(title: string | undefined, body: string | undefined): Promise<void> {
    const message = [title, body].filter((x) => !!x).join(' - ');
    if (!message) return;
    const t = await this.toast.create({ message, duration: 4000, position: 'top' });
    await t.present();
  }
}
