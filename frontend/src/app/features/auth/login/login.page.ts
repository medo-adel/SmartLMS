import { Component, ElementRef, NgZone, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { IonContent, IonButton, IonInput, IonSpinner, IonIcon } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { eyeOutline, eyeOffOutline, schoolOutline, sparklesOutline, languageOutline, flameOutline, trophyOutline, alertCircleOutline, createOutline } from 'ionicons/icons';
import { AuthService } from '../../../core/services/auth.service';
import { TenantService } from '../../../core/services/tenant.service';
import { I18nService, Lang } from '../../../core/services/i18n.service';
import { BiometricService } from '../../../core/services/biometric.service';
import { TranslatePipe } from '../../../core/pipes/translate.pipe';

/** The slice of Google's GSI client this page uses. Loaded on demand, only when the backend says Google sign-in is configured. */
interface GsiClient { accounts: { id: {
  initialize(c: { client_id: string; callback: (r: { credential?: string }) => void }): void;
  renderButton(el: HTMLElement, o: Record<string, unknown>): void;
} } }

@Component({
  selector: 'app-login', imports: [FormsModule, RouterLink, IonContent, IonButton, IonInput, IonSpinner, IonIcon, TranslatePipe],
  templateUrl: './login.page.html', styleUrl: './login.page.scss',
})
export class LoginPage {
  readonly auth = inject(AuthService);
  readonly tenant = inject(TenantService);
  readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);
  email = ''; password = ''; academy = this.tenant.tenantKey();
  readonly busy = signal(false);
  readonly error = signal('');
  readonly editAcademy = signal(false);
  readonly showPw = signal(false);
  /** Client id from GET /auth/external-providers; null hides the Google button (not configured, platform context, or probe failed). */
  readonly googleClientId = signal<string | null>(null);
  readonly socialBusy = signal(false);
  private readonly googleBtn = viewChild<ElementRef<HTMLElement>>('googleBtn');
  private gsi: Promise<GsiClient> | null = null;
  constructor() {
    addIcons({ 'eye-outline': eyeOutline, 'eye-off-outline': eyeOffOutline, 'school-outline': schoolOutline, 'sparkles-outline': sparklesOutline, 'language-outline': languageOutline, 'flame-outline': flameOutline, 'trophy-outline': trophyOutline, 'alert-circle-outline': alertCircleOutline, 'create-outline': createOutline });
    effect(() => {
      const el = this.googleBtn()?.nativeElement; const id = this.googleClientId();
      if (el && id) void this.mountGoogle(el, id);
    });
    void this.probeSocial();
  }
  get initial(): string { return (this.tenant.branding()?.name ?? this.i18n.t('app.name')).trim().charAt(0).toUpperCase(); }

  async setLang(v: unknown): Promise<void> { if (v === 'en' || v === 'ar') await this.i18n.set(v as Lang); }
  /** Empty academy = platform context (SuperAdmin sign-in without X-Tenant-Id). */
  async saveAcademy(): Promise<void> { await this.tenant.setTenant(this.academy); this.editAcademy.set(false); void this.probeSocial(); }

  /** Ask the backend which social providers it can verify; the button appears only when one is actually configured. */
  async probeSocial(): Promise<void> {
    this.googleClientId.set(null);
    if (!this.tenant.tenantKey()) return; // platform context has no social sign-in
    try {
      const g = (await this.auth.externalProviders()).find((p) => p.provider === 'google');
      if (g?.clientId) this.googleClientId.set(g.clientId);
    } catch { /* probe failed (offline, old backend): password sign-in still works */ }
  }

  private loadGsi(): Promise<GsiClient> {
    const w = window as unknown as { google?: GsiClient };
    if (w.google?.accounts?.id) return Promise.resolve(w.google);
    this.gsi ??= new Promise<GsiClient>((resolve, reject) => {
      const s = document.createElement('script');
      s.src = 'https://accounts.google.com/gsi/client'; s.async = true; s.defer = true;
      s.onload = () => resolve((window as unknown as { google: GsiClient }).google);
      s.onerror = () => reject(new Error('gsi load failed'));
      document.head.appendChild(s);
    });
    return this.gsi;
  }

  private async mountGoogle(el: HTMLElement, clientId: string): Promise<void> {
    try {
      const g = await this.loadGsi();
      g.accounts.id.initialize({ client_id: clientId, callback: (r) => { if (r.credential) void this.zone.run(() => this.socialSignIn(r.credential!)); } });
      el.replaceChildren();
      g.accounts.id.renderButton(el, { type: 'standard', theme: 'outline', size: 'large', width: Math.min(el.clientWidth || 360, 400), text: 'continue_with', locale: this.i18n.lang() });
    } catch { this.googleClientId.set(null); } // script blocked or offline: hide the button rather than strand it
  }

  async socialSignIn(idToken: string): Promise<void> {
    this.error.set('');
    this.socialBusy.set(true);
    try {
      await this.auth.socialLogin('google', idToken);
      await this.router.navigateByUrl('/app/dashboard');
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status !== 0 ? (e.error?.message ?? this.i18n.t('common.error')) : this.i18n.t('err.network'));
    } finally { this.socialBusy.set(false); }
  }

  async submit(): Promise<void> {
    this.error.set('');
    if (!/^\S+@\S+\.\S+$/.test(this.email.trim()) || !this.password) { this.error.set(this.i18n.t('login.invalid')); return; }
    this.busy.set(true);
    try {
      await this.auth.login(this.email.trim(), this.password);
      await this.router.navigateByUrl('/app/dashboard');
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status !== 0 ? (e.error?.message ?? this.i18n.t('common.error')) : this.i18n.t('err.network'));
    } finally { this.busy.set(false); }
  }
}
