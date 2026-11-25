import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { IonContent, IonButton } from '@ionic/angular';
import { AuthService } from '../../../core/services/auth.service';
import { BiometricService } from '../../../core/services/biometric.service';
import { I18nService } from '../../../core/services/i18n.service';
import { TranslatePipe } from '../../../core/pipes/translate.pipe';

/** Shown on native when biometric unlock is on and a session is stored. Success refreshes the session; it never bypasses the server. */
@Component({ selector: 'app-lock', imports: [IonContent, IonButton, TranslatePipe], templateUrl: './lock.page.html', styleUrl: './lock.page.scss' })
export class LockPage {
  private readonly auth = inject(AuthService);
  private readonly bio = inject(BiometricService);
  private readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  readonly failed = signal(false);

  async unlock(): Promise<void> {
    this.failed.set(false);
    if (await this.bio.verify(this.i18n.t('lock.text')) && (this.auth.unlock(), await this.auth.refresh())) { await this.router.navigateByUrl('/app/dashboard'); return; }
    this.failed.set(true);
  }
  async usePassword(): Promise<void> { await this.auth.clear(); await this.router.navigateByUrl('/login'); }
}
