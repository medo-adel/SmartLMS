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
import { PushService } from './push.service';

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

describe('PushService', () => {
  let ctrl: HttpTestingController; let auth: AuthService;
  const toast = { create: vi.fn(async () => ({ present: vi.fn() })) };
  beforeEach(async () => {
    localStorage.clear(); plugin.perm = 'granted'; plugin.listeners = {}; plugin.registered = 0; cap.native = true; toast.create.mockClear();
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'app/notifications', children: [] }]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), { provide: ToastController, useValue: toast }],
    });
    TestBed.inject(DeviceService).fingerprintSync = 'web-0123456789abcdef';
    TestBed.inject(TenantService).tenantKey.set('demo');
    ctrl = TestBed.inject(HttpTestingController); auth = TestBed.inject(AuthService);
  });
  afterEach(() => ctrl.verify());

  async function signIn(): Promise<void> {
    const p = auth.login('a@x.io', 'pw');
    (await vi.waitFor(() => ctrl.expectOne(`${API}/auth/login`))).flush({ success: true, data: { accessToken: 'a1', accessTokenExpiresAtUtc: '', refreshToken: 'r1', refreshTokenExpiresAtUtc: '', user }, message: null, errors: [] });
    (await vi.waitFor(() => ctrl.expectOne(`${API}/tenants/current/branding`))).flush({ success: true, data: {}, message: null, errors: [] });
    await p;
  }

  it('registers the device token with the API after sign-in on native', async () => {
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

  it('does nothing on web (no plugin calls, no HTTP)', async () => {
    cap.native = false;
    const push = TestBed.inject(PushService);
    await push.init();
    expect(plugin.registered).toBe(0);
  });

  it('does not register when the user denies the permission', async () => {
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
});
