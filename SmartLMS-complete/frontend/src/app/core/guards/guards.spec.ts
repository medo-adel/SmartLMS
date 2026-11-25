import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, UrlTree, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { authGuard, guestGuard, roleGuard } from './guards';
import { AuthService } from '../services/auth.service';
import { StorageService } from '../services/storage.service';

const user = (roles: string[]) => ({ id: 'u', tenantId: 't', fullName: 'A', email: 'a@x.io', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles, lastLoginAtUtc: null });
const run = (g: typeof authGuard, data: Record<string, unknown> = {}) => TestBed.runInInjectionContext(() => g({ data } as unknown as ActivatedRouteSnapshot, {} as never));
const path = (r: unknown) => (r instanceof UrlTree ? TestBed.inject(Router).serializeUrl(r) : r);

describe('route guards', () => {
  let auth: AuthService;
  beforeEach(() => { localStorage.clear(); TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient()] }); auth = TestBed.inject(AuthService); });
  const signIn = async (roles: string[]) => { await TestBed.inject(StorageService).setJson('session', { accessToken: 'a', refreshToken: 'r', user: user(roles) }); await auth.restore(); };

  it('authGuard sends anonymous users to /login and lets signed-in users through', async () => {
    expect(path(run(authGuard))).toBe('/login'); await signIn(['Student']); expect(run(authGuard)).toBe(true);
  });
  it('guestGuard keeps signed-in users off /login', async () => { expect(run(guestGuard)).toBe(true); await signIn(['Student']); expect(path(run(guestGuard))).toBe('/app/dashboard'); });
  it('roleGuard allows only the listed roles', async () => {
    await signIn(['Student']);
    expect(path(run(roleGuard, { roles: ['TenantAdmin'] }))).toBe('/app/dashboard');
    expect(run(roleGuard, { roles: ['Student', 'Parent'] })).toBe(true);
    await auth.clear(); expect(path(run(roleGuard, { roles: ['Student'] }))).toBe('/login');
  });
});
