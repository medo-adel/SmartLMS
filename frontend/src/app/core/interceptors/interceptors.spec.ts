import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ToastController } from '@ionic/angular';
import { environment } from '../../../environments/environment';
import { authInterceptor } from './auth.interceptor';
import { errorInterceptor } from './error.interceptor';
import { AuthService } from '../services/auth.service';
import { DeviceService } from '../services/device.service';
import { TenantService } from '../services/tenant.service';
import { StorageService } from '../services/storage.service';

const API = environment.apiBaseUrl;
const user = { id: 'u1', tenantId: 't1', fullName: 'A', email: 'a@x.io', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null };
const tokens = (n: string) => ({ success: true, data: { accessToken: 'acc-' + n, accessTokenExpiresAtUtc: '', refreshToken: 'ref-' + n, refreshTokenExpiresAtUtc: '', user }, message: null, errors: [] });

describe('HTTP interceptors', () => {
  let http: HttpClient, ctrl: HttpTestingController, auth: AuthService;
  const toast = { create: vi.fn(async () => ({ present: vi.fn() })) };
  beforeEach(async () => {
    localStorage.clear(); toast.create.mockClear();
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'login', children: [] }]), provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])), provideHttpClientTesting(), { provide: ToastController, useValue: toast }],
    });
    http = TestBed.inject(HttpClient); ctrl = TestBed.inject(HttpTestingController); auth = TestBed.inject(AuthService);
    TestBed.inject(DeviceService).fingerprintSync = 'web-0123456789abcdef';
    TestBed.inject(TenantService).tenantKey.set('demo');
    await TestBed.inject(StorageService).setJson('session', { accessToken: 'acc-0', refreshToken: 'ref-0', user });
    await auth.restore();
  });
  afterEach(() => ctrl.verify());

  it('adds JWT, X-Tenant-Id and X-Device-Id to API calls only', () => {
    http.get(`${API}/me`).subscribe();
    const r = ctrl.expectOne(`${API}/me`);
    expect(r.request.headers.get('Authorization')).toBe('Bearer acc-0');
    expect(r.request.headers.get('X-Tenant-Id')).toBe('t1'); // after sign-in the token's tenant id replaces the typed academy code
    expect(r.request.headers.get('X-Device-Id')).toBe('web-0123456789abcdef');
    r.flush({});
    http.get('https://other.example/x').subscribe();
    const o = ctrl.expectOne('https://other.example/x');
    expect(o.request.headers.has('Authorization')).toBe(false); expect(o.request.headers.has('X-Device-Id')).toBe(false);
    o.flush({});
  });

  it('omits X-Tenant-Id when the tenant key is empty (platform / SuperAdmin context)', () => {
    TestBed.inject(TenantService).tenantKey.set('');
    http.post(`${API}/auth/login`, {}).subscribe();
    const r = ctrl.expectOne(`${API}/auth/login`);
    expect(r.request.headers.has('X-Tenant-Id')).toBe(false);
    r.flush({ success: false, message: 'x' });
  });

  it('on 401 refreshes once (single-flight for parallel calls) and retries with the new token', async () => {
    const results: unknown[] = [];
    http.get(`${API}/a`).subscribe((x) => results.push(x));
    http.get(`${API}/b`).subscribe((x) => results.push(x));
    ctrl.expectOne(`${API}/a`).flush({}, { status: 401, statusText: 'x' });
    ctrl.expectOne(`${API}/b`).flush({}, { status: 401, statusText: 'x' });
    await vi.waitFor(() => ctrl.expectOne(`${API}/auth/refresh`).flush(tokens('1')));
    await vi.waitFor(() => { expect(ctrl.match(`${API}/a`).length + ctrl.match(`${API}/b`).length).toBeGreaterThanOrEqual(0); });
    const retried = await vi.waitFor(() => { const a = ctrl.expectOne(`${API}/a`); const b = ctrl.expectOne(`${API}/b`); return [a, b]; });
    for (const r of retried) { expect(r.request.headers.get('Authorization')).toBe('Bearer acc-1'); r.flush({ ok: true }); }
    expect(results.length).toBe(2);
    expect(auth.session()?.refreshToken).toBe('ref-1');
  });

  it('logout calls the API with the bearer token before clearing the session', async () => {
    const p = auth.logout();
    const r = await vi.waitFor(() => ctrl.expectOne(`${API}/auth/logout`));
    expect(r.request.headers.get('Authorization')).toBe('Bearer acc-0');
    expect(r.request.body).toEqual({ refreshToken: 'ref-0' });
    r.flush({ success: true }); await p;
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('sends no bearer token while the biometric lock is closed', () => {
    auth.unlocked.set(false);
    http.get(`${API}/me`).subscribe();
    const r = ctrl.expectOne(`${API}/me`);
    expect(r.request.headers.has('Authorization')).toBe(false); r.flush({});
  });

  it('signs out locally when the refresh is rejected', async () => {
    http.get(`${API}/a`).subscribe({ error: () => undefined });
    ctrl.expectOne(`${API}/a`).flush({}, { status: 401, statusText: 'x' });
    await vi.waitFor(() => ctrl.expectOne(`${API}/auth/refresh`).flush({ success: false }, { status: 401, statusText: 'x' }));
    await vi.waitFor(() => expect(auth.isAuthenticated()).toBe(false));
  });

  it('shows one toast for a server error and none for login failures', async () => {
    http.get(`${API}/x`).subscribe({ error: () => undefined });
    ctrl.expectOne(`${API}/x`).flush({ success: false, message: 'Boom' }, { status: 500, statusText: 'x' });
    await vi.waitFor(() => expect(toast.create).toHaveBeenCalledTimes(1));
    expect((toast.create.mock.calls[0] as unknown as [{ message: string }])[0].message).toBe('Something went wrong. Please try again.');
    http.post(`${API}/auth/login`, {}).subscribe({ error: () => undefined });
    ctrl.expectOne(`${API}/auth/login`).flush({ message: 'bad' }, { status: 400, statusText: 'x' });
    expect(toast.create).toHaveBeenCalledTimes(1);
  });
});
