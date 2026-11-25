import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { IonContent, IonLabel, IonButton, IonChip, IonSegment, IonSegmentButton } from '@ionic/angular';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { BiometricService } from '../../core/services/biometric.service';
import { DeviceService } from '../../core/services/device.service';
import { I18nService, Lang } from '../../core/services/i18n.service';
import { UserDevice } from '../../core/models/models';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

@Component({
  selector: 'app-profile', imports: [DatePipe, FormsModule, IonContent, IonLabel, IonButton, IonChip, IonSegment, IonSegmentButton, TranslatePipe],
  templateUrl: './profile.page.html', styleUrl: './profile.page.scss',
})
export class ProfilePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly device = inject(DeviceService);
  readonly auth = inject(AuthService);
  readonly bio = inject(BiometricService);
  readonly i18n = inject(I18nService);
  readonly devices = signal<UserDevice[]>([]);
  readonly bioMessage = signal('');
  readonly pwMsg = signal('');
  readonly pwErr = signal(false);
  curPw = ''; newPw = ''; newPw2 = '';

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> { try { this.devices.set(await this.api.devices()); } catch { /* toast by interceptor */ } }
  async remove(d: UserDevice): Promise<void> { try { await this.api.removeDevice(d.id); await this.load(); } catch { /* toast by interceptor */ } }
  async setLang(v: unknown): Promise<void> { if (v === 'en' || v === 'ar') await this.i18n.set(v as Lang); }
  async toggleBio(): Promise<void> {
    this.bioMessage.set('');
    if (this.bio.enabled()) { await this.bio.disable(); return; }
    try { if (!(await this.bio.enable(this.i18n.t('profile.biometric')))) this.bioMessage.set(this.i18n.t('profile.biometric.unavailable')); }
    catch { this.bioMessage.set(this.i18n.t('common.error')); }
  }
  isThisDevice(): string { return this.device.fingerprintSync; }

  async changePassword(): Promise<void> {
    this.pwMsg.set(''); this.pwErr.set(false);
    if (!this.curPw || this.newPw.length < 8) { this.pwMsg.set('profile.pwShort'); this.pwErr.set(true); return; }
    if (this.newPw !== this.newPw2) { this.pwMsg.set('profile.pwMismatch'); this.pwErr.set(true); return; }
    try { await this.api.changePassword(this.curPw, this.newPw); this.curPw = this.newPw = this.newPw2 = ''; this.pwMsg.set('profile.pwChanged'); }
    catch { this.pwMsg.set('profile.pwFailed'); this.pwErr.set(true); }
  }
}
