import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../../../environments/environment';
import { AdminService } from './admin.service';

const API = environment.apiBaseUrl;

/** URL and body shapes match the backend controllers verbatim (UsersController, TenantsController, TenantSettingsController, LmsController). */
describe('AdminService', () => {
  let http: HttpClient, ctrl: HttpTestingController, api: AdminService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpClient); ctrl = TestBed.inject(HttpTestingController); api = TestBed.inject(AdminService);
  });
  afterEach(() => ctrl.verify());

  it('searches users with search, role and paging params', () => {
    void api.users('ad', 'Instructor', 2);
    const r = ctrl.expectOne((q) => q.url === `${API}/users`);
    expect(r.request.params.get('search')).toBe('ad');
    expect(r.request.params.get('role')).toBe('Instructor');
    expect(r.request.params.get('page')).toBe('2');
    r.flush({ success: true, data: { totalCount: 0, page: 2, pageSize: 20, items: [] }, message: null, errors: [] });
  });

  it('creates a user with the CreateUserRequest shape', () => {
    void api.createUser({ fullName: 'A', email: 'a@x.io', password: 'secret', roles: ['Student'], phoneNumber: null, preferredLanguage: 'ar' });
    const r = ctrl.expectOne(`${API}/users`);
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({ fullName: 'A', email: 'a@x.io', password: 'secret', roles: ['Student'], phoneNumber: null, preferredLanguage: 'ar' });
    r.flush({ success: true, data: {}, message: null, errors: [] });
  });

  it('approves and rejects courses with the verbatim endpoints', () => {
    void api.approveCourse('c1');
    const a = ctrl.expectOne(`${API}/courses/c1/approve`);
    expect(a.request.method).toBe('POST');
    a.flush({ success: true, data: {}, message: null, errors: [] });
    void api.rejectCourse('c1', 'needs work');
    const r = ctrl.expectOne(`${API}/courses/c1/reject`);
    expect(r.request.body).toEqual({ comment: 'needs work' });
    r.flush({ success: true, data: {}, message: null, errors: [] });
  });

  it('creates a tenant with the admin seed fields and grants support access', () => {
    void api.createTenant({ name: 'A', slug: 'a', subdomain: null, adminFullName: 'N', adminEmail: 'a@x.io', adminPassword: 'pw', maxMobileDevices: 1, maxWebDevices: 1, defaultLanguage: 'ar', timeZoneId: 'Africa/Cairo', currency: 'EGP' });
    const c = ctrl.expectOne(`${API}/tenants`);
    expect(c.request.body.adminEmail).toBe('a@x.io');
    c.flush({ success: true, data: {}, message: null, errors: [] });
    void api.grantSupport('t1', 60);
    const g = ctrl.expectOne(`${API}/tenants/t1/support-access`);
    expect(g.request.body).toEqual({ minutes: 60 });
    g.flush({ success: true, data: { tenantId: 't1', expiresAtUtc: '' }, message: null, errors: [] });
  });

  it('toggles a feature flag with SetFeatureFlagRequest', () => {
    void api.setFeatureFlag('Ai.Tutor', false);
    const r = ctrl.expectOne(`${API}/tenant/feature-flags/Ai.Tutor`);
    expect(r.request.method).toBe('PUT');
    expect(r.request.body).toEqual({ isEnabled: false });
    r.flush({ success: true, data: { key: 'Ai.Tutor', isEnabled: false }, message: null, errors: [] });
  });
});
