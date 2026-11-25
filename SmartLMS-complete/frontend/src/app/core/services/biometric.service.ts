import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Capacitor } from '@capacitor/core';
import { BiometricAuth } from '@aparajita/capacitor-biometric-auth';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { StorageService } from './storage.service';

/**
 * Biometric unlock of an already-stored session. Native only: in a browser it reports unavailable
 * instead of pretending. Never stores a password; the stored refresh session is what gets unlocked.
 */
@Injectable({ providedIn: 'root' })
export class BiometricService {
  private readonly storage = inject(StorageService);
  private readonly http = inject(HttpClient);
  readonly enabled = signal(false);
  readonly available = signal(false);

  async init(): Promise<void> {
    this.enabled.set((await this.storage.get('biometricEnabled')) === '1');
    this.available.set(await this.checkAvailable());
  }
  async checkAvailable(): Promise<boolean> {
    if (!Capacitor.isNativePlatform()) return false;
    try { return (await BiometricAuth.checkBiometry()).isAvailable; } catch { return false; }
  }
  /** Returns true only when the OS confirmed the user. */
  async verify(reason: string): Promise<boolean> {
    if (!(await this.checkAvailable())) return false;
    try { await BiometricAuth.authenticate({ reason, allowDeviceCredential: true }); return true; } catch { return false; }
  }
  async enable(reason: string): Promise<boolean> {
    if (!(await this.verify(reason))) return false;
    await firstValueFrom(this.http.post(`${environment.apiBaseUrl}/auth/biometric/enable`, {}));
    await this.storage.set('biometricEnabled', '1');
    this.enabled.set(true);
    return true;
  }
  async disable(): Promise<void> { await this.storage.remove('biometricEnabled'); this.enabled.set(false); }
}
