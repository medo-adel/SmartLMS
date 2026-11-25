import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { IonContent, IonButton, IonInput, IonSpinner } from '@ionic/angular';
import { AuthService } from '../../../core/services/auth.service';
import { TenantService } from '../../../core/services/tenant.service';
import { I18nService, Lang } from '../../../core/services/i18n.service';
import { TranslatePipe } from '../../../core/pipes/translate.pipe';

type Mode = 'register' | 'forgot';

/**
 * Self-service account flows that the API supports but the app did not surface:
 * register + email verification (mode "register") and forgot/reset password (mode "forgot").
 * The API rate-limits these endpoints and emails the code; in Development the code is also logged by the backend.
 */
@Component({
  selector: 'app-register', imports: [FormsModule, RouterLink, IonContent, IonButton, IonInput, IonSpinner, TranslatePipe],
  templateUrl: './register.page.html', styleUrl: './register.page.scss',
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  readonly tenant = inject(TenantService);
  readonly i18n = inject(I18nService);
  readonly mode: Mode;
  step: 'form' | 'code' | 'done' = 'form';
  fullName = ''; email = ''; password = ''; phone = ''; academy = this.tenant.tenantKey();
  /** Public sign-up creates exactly one of these two roles (API-enforced); staff roles come from the academy admin. */
  role: 'Student' | 'Parent' = 'Student';
  code = ''; newPassword = '';
  readonly busy = signal(false);
  readonly error = signal('');
  readonly info = signal('');

  constructor() { this.mode = (inject(ActivatedRoute).snapshot.data['mode'] as Mode) ?? 'register'; }
  get isForgot(): boolean { return this.mode === 'forgot'; }
  async setLang(v: unknown): Promise<void> { if (v === 'en' || v === 'ar') await this.i18n.set(v as Lang); }

  private fail(e: unknown): void {
    this.error.set(e instanceof HttpErrorResponse && e.status !== 0 ? (e.error?.message ?? this.i18n.t('common.error')) : this.i18n.t('err.network'));
  }

  async submitForm(): Promise<void> {
    this.error.set(''); this.info.set('');
    const email = this.email.trim();
    if (this.isForgot) {
      if (!/^\S+@\S+\.\S+$/.test(email)) { this.error.set(this.i18n.t('login.invalid')); return; }
      this.busy.set(true);
      try { await this.auth.forgotPassword(email); this.step = 'code'; this.info.set(this.i18n.t('reg.codeSent')); }
      catch (e) { this.fail(e); } finally { this.busy.set(false); }
      return;
    }
    if (!this.fullName.trim() || !/^\S+@\S+\.\S+$/.test(email) || this.password.length < 10) { this.error.set(this.i18n.t('reg.invalid')); return; }
    this.busy.set(true);
    try {
      await this.tenant.setTenant(this.academy);
      await this.auth.register({ fullName: this.fullName.trim(), email, password: this.password, phoneNumber: this.phone.trim() || null, preferredLanguage: this.i18n.lang(), role: this.role });
      this.step = 'code'; this.info.set(this.i18n.t('reg.codeSent'));
    } catch (e) { this.fail(e); } finally { this.busy.set(false); }
  }

  async submitCode(): Promise<void> {
    this.error.set('');
    if (!this.code.trim()) return;
    if (this.isForgot && this.newPassword.length < 10) { this.error.set(this.i18n.t('reg.invalid')); return; }
    this.busy.set(true);
    try {
      if (this.isForgot) await this.auth.resetPassword(this.email.trim(), this.code.trim(), this.newPassword);
      else await this.auth.verifyEmail(this.email.trim(), this.code.trim());
      this.step = 'done';
    } catch (e) { this.fail(e); } finally { this.busy.set(false); }
  }

  /** Resend the verification code (POST /auth/otp/request). Register only: forgot-password resubmits its own form. */
  async resend(): Promise<void> {
    this.error.set(''); this.busy.set(true);
    try { await this.auth.resendVerification(this.email.trim()); this.info.set(this.i18n.t('reg.codeSent')); }
    catch (e) { this.fail(e); } finally { this.busy.set(false); }
  }
}
