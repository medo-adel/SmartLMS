import { Injectable } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { PrivacyScreen } from '@capacitor-community/privacy-screen';

/**
 * Native screenshot and screen-recording protection (Capacitor Privacy Screen). It is switched on while a video lesson or an exam is open
 * and off again when the screen closes. On the web there is nothing to switch, so this does nothing there (the page-level watermark and
 * blur cover remain the only deterrent). Not verified on a real device.
 */
@Injectable({ providedIn: 'root' })
export class PrivacyService {
  private count = 0;
  async enable(): Promise<void> {
    this.count++;
    if (!Capacitor.isNativePlatform()) return;
    try { await PrivacyScreen.enable(); } catch { /* plugin missing or unsupported on this device */ }
  }
  async disable(): Promise<void> {
    this.count = Math.max(0, this.count - 1);
    if (!Capacitor.isNativePlatform() || this.count > 0) return;
    try { await PrivacyScreen.disable(); } catch { /* ignore */ }
  }
}
