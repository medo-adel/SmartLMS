import { Injectable, signal } from '@angular/core';
import { Network } from '@capacitor/network';

/** Online/offline state. Capacitor Network uses the browser events on the web and the OS connectivity callbacks on a device. */
@Injectable({ providedIn: 'root' })
export class NetworkService {
  private readonly _online = signal(true);
  readonly online = this._online.asReadonly();

  constructor() {
    void Network.getStatus().then((s) => this._online.set(s.connected)).catch(() => undefined);
    void Network.addListener('networkStatusChange', (s) => this._online.set(s.connected));
  }
}
