import { Injectable, inject } from '@angular/core';
import { ToastController } from '@ionic/angular';

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly ctrl = inject(ToastController);
  private last = '';
  private lastAt = 0;
  /** On phones the floating tab bar is the anchor, so the toast sits above navigation instead of over it. */
  private anchor(): HTMLElement | undefined {
    const bar = typeof document === 'undefined' ? null : document.querySelector<HTMLElement>('ion-tab-bar');
    return bar && bar.offsetParent !== null && bar.getBoundingClientRect().height > 0 ? bar : undefined;
  }
  async error(message: string): Promise<void> {
    const now = Date.now();
    if (message === this.last && now - this.lastAt < 3000) return; // collapse bursts of identical errors
    this.last = message; this.lastAt = now;
    const t = await this.ctrl.create({ message, duration: 4000, color: 'danger', position: 'bottom', positionAnchor: this.anchor() });
    await t.present();
  }
}
