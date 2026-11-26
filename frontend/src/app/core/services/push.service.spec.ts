import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { ToastController } from '@ionic/angular';
import { environment } from '../../../environments/environment';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { AuthService } from './auth.service';
import { DeviceService } from './device.service';
import { TenantService } from './tenant.service';
import { PushService, WEB_PUSH_DRIVER } from './push.service';

const API = environment.apiBaseUrl;
const cap = { native: true, platform: 'android' };
const plugin = {
  perm: 'granted' as 'granted' | 'denied' | 'prompt',
  listeners: {} as Record<string, (v: { value?: string; title?: string; body?: string }) => void>,
  registered: 0,
};
vi.mock('@capacitor/core', async (orig) => ({ ...(await orig<typeof import('@capacitor/core')>()), Capacitor: { isNativePlatform: () => cap.native, getPlatform: () => cap.platform } }));
vi.mock('@capacitor/push-notifications', () => ({
  PushNotifications: {
    checkPermissions: vi.fn(async () => ({ receive: plugin.perm })),
    requestPermissions: vi.fn(async () => ({ receive: plugin.perm })),
    addListener: vi.fn(async (event: string, cb: (v: { value?: string; title?: string; body?: string }) => void) => { plugin.listeners[event] = cb; }),
    register: vi.fn(async () => { plugin.registered++; }),
  },
}));

const user = { id: 'u1', tenantId: 't1', fullName: 'A', email: 'a@x.io', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null };
const webCfg = {
  enabled: true,
  vapidPublicKey: 'vapid-pub',
  firebase: { apiKey: 'ak', authDomain: 'p.firebaseapp.com', projectId: 'p', messagingSenderId: '99', appId: 'app' },
};

describe('PushService', () => {
  let ctrl: HttpTestingController; let auth: AuthService;
  const toast = { create: vi.fn(async () => ({ present: vi.fn() })) };
  const swReg = { scope: 'https://app.test/' } as ServiceWorkerRegistration;
  const sw = { register: vi.fn(async (_url: string) => swReg) };
  const driver = { getToken: vi.fn(async (_r: ServiceWorkerRegistration, _v: string, _c: object) => 'web-token-1' as string | null), deleteToken: vi.fn(async () => {}), onForeground: vi.fn((_cb: (t?: string, b?: string) => void) => {}) };

  beforeEach(async () => {
    localStorage.clear(); plugin.perm = 'granted'; plugin.listeners = {}; plugin.registered = 0; cap.native = true; cap.platform = 'android'; toast.create.mockClear();
    sw.register.mockClear(); driver.getToken.mockClear(); driver.deleteToken.mockClear(); driver.onForeground.mockClear();
    driver.getToken.mockResolvedValue('web-token-1');
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'app/notifications', children: [] }]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), { provide: ToastController, useValue: toast }, { provide: WEB_PUSH_DRIVER, useValue: driver }],
    });
    TestBed.inject(DeviceService).fingerprintSync = 'web-0123456789abcdef';
    TestBed.inject(TenantService).tenantKey.set('demo');
    ctrl = TestBed.inject(HttpTestingController); auth = TestBed.inject(AuthService);
  });
  afterEach(() => { ctrl.verify(); delete (navigator as { serviceWorker?: unknown }).serviceWorker; delete (window as { PushManager?: unknown }).PushManager; });

  function withSw(): void {
    Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: sw });
    Object.defineProperty(window, 'PushManager', { configurable: true, value: class {} });
  }

  async function signIn(): Promise<void> {
    const p = auth.login('a@x.io', 'pw');
    (await vi.waitFor(() => ctrl.expectOne(`${API}/auth/login`))).flush({ success: true, data: { accessToken: 'a1', accessTokenExpiresAtUtc: '', refreshToken: 'r1', refreshTokenExpiresAtUtc: '', user }, message: null, errors: [] });
    (await vi.waitFor(() => ctrl.expectOne(`${API}/tenants/current/branding`))).flush({ success: true, data: {}, message: null, errors: [] });
    await p;
  }

  it('registers the device token with the API after sign-in on native Android', async () => {
    TestBed.inject(PushService);
    await signIn();
    await vi.waitFor(() => { expect(plugin.registered).toBe(1); });
    expect(plugin.listeners['registration']).toBeTruthy();
    plugin.listeners['registration']({ value: 'fcm-token-1' });
    const r = await vi.waitFor(() => ctrl.expectOne(`${API}/notifications/push-token`));
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({ fcmToken: 'fcm-token-1', platform: 'Android' });
    r.flush({ success: true, data: null, message: 'Registered.', errors: [] });
  });

  it('skips registration entirely on native iOS (APNs token is unusable by the FCM sender)', async () => {
    cap.platform = 'ios';
    TestBed.inject(PushService);
    await signIn();
    await new Promise((r) => setTimeout(r, 50));
    expect(plugin.registered).toBe(0);
    expect(plugin.listeners['registration']).toBeUndefined();
    ctrl.expectNone(`${API}/notifications/push-token`);
    ctrl.expectNone(`${API}/notifications/push-config`);
  });

  it('does not register when the user denies the native permission', async () => {
    plugin.perm = 'denied';
    const push = TestBed.inject(PushService);
    await push.init();
    expect(plugin.registered).toBe(0);
    expect(plugin.listeners['registration']).toBeUndefined();
  });

  it('shows a toast for a foreground notification and opens notifications on tap', async () => {
    const router = TestBed.inject(Router);
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const push = TestBed.inject(PushService);
    await push.init();
    await plugin.listeners['pushNotificationReceived']({ title: 'تم القبول', body: 'كورسك اتنشر' });
    await vi.waitFor(() => { expect(toast.create).toHaveBeenCalled(); });
    expect((toast.create.mock.calls[0] as unknown as [{ message: string }])[0].message).toContain('تم القبول');
    await plugin.listeners['pushNotificationActionPerformed']({});
    expect(nav).toHaveBeenCalledWith('/app/notifications');
  });

  it('web: full server config registers the service worker and posts the FCM web token', async () => {
    cap.native = false; withSw();
    TestBed.inject(PushService);
    await signIn();
    const c = await vi.waitFor(() => ctrl.expectOne(`${API}/notifications/push-config`));
    c.flush({ success: true, data: webCfg, message: null, errors: [] });
    await vi.waitFor(() => { expect(sw.register).toHaveBeenCalled(); });
    const swUrl = sw.register.mock.calls[0][0];
    expect(swUrl).toContain('/firebase-messaging-sw.js?');
    expect(swUrl).toContain('apiKey=ak'); expect(swUrl).toContain('projectId=p'); expect(swUrl).toContain('messagingSenderId=99'); expect(swUrl).toContain('appId=app'); expect(swUrl).toContain('authDomain=p.firebaseapp.com');
    expect(driver.getToken).toHaveBeenCalledWith(swReg, 'vapid-pub', webCfg.firebase);
    const r = await vi.waitFor(() => ctrl.expectOne(`${API}/notifications/push-token`));
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({ fcmToken: 'web-token-1', platform: 'Web' });
    r.flush({ success: true, data: null, message: 'Registered.', errors: [] });
    await vi.waitFor(() => { expect(driver.onForeground).toHaveBeenCalled(); });
  });

  it('web: disabled or incomplete server config skips registration (in-app notifications remain)', async () => {
    cap.native = false; withSw();
    TestBed.inject(PushService);
    await signIn();
    (await vi.waitFor(() => ctrl.expectOne(`${API}/notifications/push-config`))).flush({ success: true, data: { enabled: false, vapidPublicKey: null, firebase: null }, message: null, errors: [] });
    await new Promise((r) => setTimeout(r, 50));
    expect(sw.register).not.toHaveBeenCalled();
    expect(driver.getToken).not.toHaveBeenCalled();
    ctrl.expectNone(`${API}/notifications/push-token`);
  });

  it('web: a denied notification permission is silent (no token POST)', async () => {
    cap.native = false; withSw();
    driver.getToken.mockResolvedValue(null); // getToken resolves null when the user denies / FCM is unreachable
    TestBed.inject(PushService);
    await signIn();
    (await vi.waitFor(() => ctrl.expectOne(`${API}/notifications/push-config`))).flush({ success: true, data: webCfg, message: null, errors: [] });
    await vi.waitFor(() => { expect(driver.getToken).toHaveBeenCalled(); });
    await new Promise((r) => setTimeout(r, 50));
    ctrl.expectNone(`${API}/notifications/push-token`);
    expect(toast.create).not.toHaveBeenCalled();
  });

  it('web: a browser without service-worker support skips everything (no HTTP)', async () => {
    cap.native = false; // no withSw(): jsdom has neither navigator.serviceWorker nor window.PushManager
    TestBed.inject(PushService);
    await signIn();
    await new Promise((r) => setTimeout(r, 50));
    expect(driver.getToken).not.toHaveBeenCalled();
    ctrl.expectNone(`${API}/notifications/push-config`);
    ctrl.expectNone(`${API}/notifications/push-token`);
  });

  it('logout unregisters exactly this device: DELETE with the registered token, then FCM deleteToken', async () => {
    cap.native = false; withSw();
    TestBed.inject(PushService);
    await signIn();
    (await vi.waitFor(() => ctrl.expectOne(`${API}/notifications/push-config`))).flush({ success: true, data: webCfg, message: null, errors: [] });
    (await vi.waitFor(() => ctrl.expectOne(`${API}/notifications/push-token`))).flush({ success: true, data: null, message: null, errors: [] });
    await new Promise((r) => setTimeout(r, 20));   // let registerToken's continuation record the token before logout reads it
    const out = auth.logout();
    const del = await vi.waitFor(() => ctrl.expectOne((r) => r.method === 'DELETE' && r.url === `${API}/notifications/push-token`));
    expect(del.request.body).toEqual({ fcmToken: 'web-token-1' });
    del.flush({ success: true, data: null, message: 'Removed.', errors: [] });
    (await vi.waitFor(() => ctrl.expectOne(`${API}/auth/logout`))).flush({ success: true, data: null, message: null, errors: [] });
    await out;
    expect(driver.deleteToken).toHaveBeenCalled();
  });
});
