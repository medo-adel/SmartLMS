import { Injectable, inject, signal, computed } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { Session, TokenResponse, User, DashboardKind } from '../models/models';
import { StorageService } from './storage.service';
import { DeviceService } from './device.service';
import { TenantService } from './tenant.service';
import { SKIP_TENANT } from '../interceptors/auth.interceptor';

const KEY = 'session';

/** Which dashboard a set of roles gets. Highest privilege wins. */
export function dashboardFor(roles: readonly string[]): DashboardKind {
  // Mirrors the API: /dashboards/admin = TenantAdmin only, /dashboards/instructor = Instructor only,
  // so SuperAdmin and TeachingAssistant would get 403 there and see the "no dashboard" page instead.
  if (roles.includes('TenantAdmin')) return 'admin';
  if (roles.includes('Instructor')) return 'instructor';
  if (roles.includes('Parent')) return 'parent';
  if (roles.includes('Student')) return 'student';
  return 'none';
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(StorageService);
  private readonly device = inject(DeviceService);
  private readonly tenant = inject(TenantService);
  private readonly _session = signal<Session | null>(null);
  private refreshing: Promise<boolean> | null = null;

  readonly session = this._session.asReadonly();
  readonly user = computed<User | null>(() => this._session()?.user ?? null);
  readonly isAuthenticated = computed(() => this._session() !== null);
  readonly dashboard = computed<DashboardKind>(() => dashboardFor(this._session()?.user.roles ?? []));
  /** False after a cold start with a stored session until the user passes the biometric lock (native only). */
  readonly unlocked = signal(false);

  /** Token for requests. Null while the biometric lock is closed, so a locked app sends nothing. */
  get accessToken(): string | null { return this.unlocked() ? (this._session()?.accessToken ?? null) : null; }
  unlock(): void { this.unlocked.set(true); }
  hasRole(...roles: string[]): boolean { const r = this._session()?.user.roles ?? []; return roles.some((x) => r.includes(x)); }

  async restore(): Promise<void> {
    const s = await this.storage.getJson<Session>(KEY);
    if (s?.accessToken && s.refreshToken) {
      this._session.set(s); this.tenant.adoptTenantId(s.user.tenantId);
      // With biometric unlock on (native), the stored session stays unusable until the OS confirms the user.
      this.unlocked.set(!(Capacitor.isNativePlatform() && (await this.storage.get('biometricEnabled')) === '1'));
    }
  }
  async login(email: string, password: string): Promise<void> {
    const d = await this.device.init();
    const body = { email, password, device: { fingerprint: d.fingerprint, deviceType: d.deviceType, platform: d.platform, deviceName: d.deviceName } };
    try {
      await this.loginWith(body, false);
    } catch (err) {
      // Platform sign-in under a stale academy key: the API scopes user names per tenant, so a SuperAdmin
      // resolves to nothing under X-Tenant-Id (401 INVALID_CREDENTIALS, no lockout increment: no account
      // matched). Retry once without the tenant header, on EXACTLY that code: arbitrary 401s, network
      // errors and locked accounts never trigger the retry. If the retry also fails, the caller sees the
      // ORIGINAL error. A tenant user with a wrong password gets the same 401 either way: the headerless
      // retry matches no account (names are tenant-scoped), so it never touches the lockout counter.
      const code = err instanceof HttpErrorResponse ? (err.error as { code?: string } | null)?.code : undefined;
      if (!(err instanceof HttpErrorResponse) || err.status !== 401 || code !== 'INVALID_CREDENTIALS' || !this.tenant.tenantKey()) throw err;
      try { await this.loginWith(body, true); } catch { throw err; }
    }
  }
  private async loginWith(body: unknown, skipTenant: boolean): Promise<void> {
    const options = skipTenant ? { context: new HttpContext().set(SKIP_TENANT, true) } : {};
    const res = await firstValueFrom(this.http.post<ApiResponse<TokenResponse>>(`${environment.apiBaseUrl}/auth/login`, body, options));
    if (!res.success || !res.data) throw new Error(res.message ?? 'login failed');
    await this.setSession(res.data);
    // A fresh platform login (no tenant on the token) must not inherit a previous user's academy:
    // a stale X-Tenant-Id would both break platform routes and send SuperAdmin calls to the wrong tenant.
    if (!res.data.user.tenantId) await this.tenant.setTenant('');
    await this.tenant.loadBranding();
  }
  /** Self-service sign-up inside the current academy (X-Tenant-Id from the interceptor). The API emails a verification code. Public registration allows Student or Parent only; the API rejects anything else. */
  async register(input: { fullName: string; email: string; password: string; phoneNumber?: string | null; preferredLanguage?: string | null; role?: 'Student' | 'Parent' }): Promise<void> {
    const res = await firstValueFrom(this.http.post<ApiResponse<unknown>>(`${environment.apiBaseUrl}/auth/register`, {
      fullName: input.fullName, email: input.email, password: input.password,
      phoneNumber: input.phoneNumber ?? null, preferredLanguage: input.preferredLanguage ?? null, role: input.role ?? null,
    }));
    if (!res.success) throw new Error(res.message ?? 'registration failed');
  }
  /** How verification codes actually leave the server (GET /auth/mail-delivery): mailbox | local-outbox | disabled. Drives truthful copy on the code step. */
  async mailDeliveryMode(): Promise<'mailbox' | 'local-outbox' | 'disabled' | 'unknown'> {
    try {
      const res = await firstValueFrom(this.http.get<ApiResponse<{ mode: string }>>(`${environment.apiBaseUrl}/auth/mail-delivery`));
      const m = res.data?.mode;
      return m === 'mailbox' || m === 'local-outbox' || m === 'disabled' ? m : 'unknown';
    } catch { return 'unknown'; } // older backend without the route: neutral copy, never promise an inbox
  }
  /** Resends an email-verification code (POST /auth/otp/request). The response is identical whether or not the address is registered. */
  async resendVerification(email: string): Promise<void> {    const res = await firstValueFrom(this.http.post<ApiResponse<unknown>>(`${environment.apiBaseUrl}/auth/otp/request`, { destination: email, purpose: 'EmailVerification' }));
    if (!res.success) throw new Error(res.message ?? 'request failed');
  }
  async verifyEmail(email: string, code: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<ApiResponse<unknown>>(`${environment.apiBaseUrl}/auth/verify-email`, { email, code }));
    if (!res.success) throw new Error(res.message ?? 'verification failed');
  }
  async forgotPassword(email: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<ApiResponse<unknown>>(`${environment.apiBaseUrl}/auth/forgot-password`, { email }));
    if (!res.success) throw new Error(res.message ?? 'request failed');
  }
  async resetPassword(email: string, code: string, newPassword: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<ApiResponse<unknown>>(`${environment.apiBaseUrl}/auth/reset-password`, { email, code, newPassword }));
    if (!res.success) throw new Error(res.message ?? 'reset failed');
  }
  /** Single-flight: concurrent 401s share one refresh call so the rotating refresh token is used once. */
  refresh(): Promise<boolean> {
    if (this.refreshing) return this.refreshing;
    this.refreshing = this.doRefresh().finally(() => { this.refreshing = null; });
    return this.refreshing;
  }
  private async doRefresh(): Promise<boolean> {
    const cur = this._session();
    if (!cur) return false;
    try {
      const d = await this.device.init();
      const res = await firstValueFrom(this.http.post<ApiResponse<TokenResponse>>(`${environment.apiBaseUrl}/auth/refresh`,
        { refreshToken: cur.refreshToken, deviceFingerprint: d.fingerprint }));
      if (!res.success || !res.data) throw new Error('refresh rejected');
      await this.setSession(res.data);
      return true;
    } catch { await this.clear(); return false; }
  }
  async logout(): Promise<void> {
    const cur = this._session();
    // /auth/logout requires the bearer token, so revoke on the server first, then clear locally.
    if (cur && this.unlocked()) { try { await firstValueFrom(this.http.post(`${environment.apiBaseUrl}/auth/logout`, { refreshToken: cur.refreshToken })); } catch { /* offline or expired: still sign out locally */ } }
    await this.clear();
  }
  async clear(): Promise<void> { this.unlocked.set(false); this._session.set(null); await this.storage.remove(KEY); }
  private async setSession(t: TokenResponse): Promise<void> {
    this.unlocked.set(true);
    const s: Session = { accessToken: t.accessToken, refreshToken: t.refreshToken, user: t.user };
    this._session.set(s);
    this.tenant.adoptTenantId(t.user.tenantId);
    await this.storage.setJson(KEY, s);
  }
}
