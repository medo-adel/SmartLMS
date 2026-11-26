import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { DeviceService } from './device.service';
import { TenantService } from './tenant.service';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { dashboardFor, AuthService } from './auth.service';

describe('dashboardFor', () => {
  it('maps roles to the right dashboard, highest privilege first', () => {
    expect(dashboardFor(['Student'])).toBe('student');
    expect(dashboardFor(['Parent'])).toBe('parent');
    expect(dashboardFor(['Instructor'])).toBe('instructor');
    // API: instructor dashboard is Instructor-only, admin dashboard is TenantAdmin-only
    expect(dashboardFor(['TeachingAssistant'])).toBe('none');
    expect(dashboardFor(['SuperAdmin'])).toBe('none');
    expect(dashboardFor(['TenantAdmin'])).toBe('admin');
    expect(dashboardFor(['Student', 'TenantAdmin'])).toBe('admin');
    expect(dashboardFor([])).toBe('none');
  });
});

const API = environment.apiBaseUrl;
const socialUser = { id: 'u-g', tenantId: 't-1', fullName: 'Social User', email: 'social@mail.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null };

describe('AuthService social sign-in', () => {
  let ctrl: HttpTestingController; let auth: AuthService; let tenant: TenantService;
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()] });
    TestBed.inject(DeviceService).fingerprintSync = 'web-0123456789abcdef';
    tenant = TestBed.inject(TenantService); tenant.tenantKey.set('demo');
    ctrl = TestBed.inject(HttpTestingController); auth = TestBed.inject(AuthService);
  });
  afterEach(() => ctrl.verify());

  it('externalProviders returns the configured providers from the API', async () => {
    const p = auth.externalProviders();
    ctrl.expectOne(`${API}/auth/external-providers`).flush({ success: true, data: { providers: [{ provider: 'google', clientId: 'cid-1' }] }, message: null, errors: [] });
    expect(await p).toEqual([{ provider: 'google', clientId: 'cid-1' }]);
  });

  it('socialLogin posts the id token with the device and stores the session', async () => {
    const branding = vi.spyOn(tenant, 'loadBranding').mockResolvedValue();
    const p = auth.socialLogin('google', 'id-token-1');
    const r = await vi.waitFor(() => ctrl.expectOne(`${API}/auth/social/google`)); // request fires after device.init() resolves
    expect(r.request.method).toBe('POST');
    expect(r.request.body.idToken).toBe('id-token-1');
    expect(r.request.body.device.fingerprint).toBeTruthy();
    expect(r.request.headers.get('X-Tenant-Id')).toBeTruthy();
    r.flush({ success: true, data: { accessToken: 'acc-g', accessTokenExpiresAtUtc: '', refreshToken: 'ref-g', refreshTokenExpiresAtUtc: '', user: socialUser }, message: null, errors: [] });
    await p;
    expect(auth.user()?.id).toBe('u-g');
    expect(branding).toHaveBeenCalled();
  });

  it('socialLogin surfaces an API failure (e.g. EXTERNAL_IDENTITY_IN_USE)', async () => {
    const p = auth.socialLogin('google', 'id-token-1');
    (await vi.waitFor(() => ctrl.expectOne(`${API}/auth/social/google`))).flush({ success: false, data: null, message: 'linked elsewhere', errors: [] }, { status: 409, statusText: 'Conflict' });
    await expect(p).rejects.toBeTruthy();
    expect(auth.isAuthenticated()).toBe(false);
  });
});
