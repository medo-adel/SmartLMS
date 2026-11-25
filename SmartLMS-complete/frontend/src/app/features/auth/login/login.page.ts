import { Component, inject, signal } from '@angular/core';
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

@Component({
  selector: 'app-login', imports: [FormsModule, RouterLink, IonContent, IonButton, IonInput, IonSpinner, IonIcon, TranslatePipe],
  templateUrl: './login.page.html', styleUrl: './login.page.scss',
})
export class LoginPage {
  readonly auth = inject(AuthService);
  readonly tenant = inject(TenantService);
  readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  email = ''; password = ''; academy = this.tenant.tenantKey();
  readonly busy = signal(false);
  readonly error = signal('');
  readonly editAcademy = signal(false);
  readonly showPw = signal(false);
  constructor() { addIcons({ 'eye-outline': eyeOutline, 'eye-off-outline': eyeOffOutline, 'school-outline': schoolOutline, 'sparkles-outline': sparklesOutline, 'language-outline': languageOutline, 'flame-outline': flameOutline, 'trophy-outline': trophyOutline, 'alert-circle-outline': alertCircleOutline, 'create-outline': createOutline }); }
  get initial(): string { return (this.tenant.branding()?.name ?? this.i18n.t('app.name')).trim().charAt(0).toUpperCase(); }

  async setLang(v: unknown): Promise<void> { if (v === 'en' || v === 'ar') await this.i18n.set(v as Lang); }
  /** Empty academy = platform context (SuperAdmin sign-in without X-Tenant-Id). */
  async saveAcademy(): Promise<void> { await this.tenant.setTenant(this.academy); this.editAcademy.set(false); }

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
