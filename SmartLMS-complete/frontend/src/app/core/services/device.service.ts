import { Injectable, inject } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { Device } from '@capacitor/device';
import { StorageService } from './storage.service';

export interface DeviceInfo { fingerprint: string; deviceType: string; platform: string; deviceName: string; }

/** Provides the stable device id sent as X-Device-Id (device binding). */
@Injectable({ providedIn: 'root' })
export class DeviceService {
  private readonly storage = inject(StorageService);
  private cached: DeviceInfo | null = null;
  /** Synchronous copy for the HTTP interceptor; set once init() ran. */
  fingerprintSync = '';

  async init(): Promise<DeviceInfo> {
    if (this.cached) return this.cached;
    const native = Capacitor.isNativePlatform();
    let fingerprint = '';
    let name = 'Browser';
    let platform: string = Capacitor.getPlatform();
    try {
      const info = await Device.getInfo();
      name = info.name || `${info.manufacturer ?? ''} ${info.model ?? ''}`.trim() || name;
      platform = info.platform;
      if (native) fingerprint = `${info.platform}-${(await Device.getId()).identifier}`;
    } catch { /* plugin unavailable: fall through to the persisted id */ }
    if (fingerprint.length < 16) {
      let id = await this.storage.get('deviceId');
      if (!id || id.length < 16) { id = `web-${crypto.randomUUID()}`; await this.storage.set('deviceId', id); }
      fingerprint = id;
    }
    this.cached = { fingerprint, deviceType: native ? 'Mobile' : 'Web', platform, deviceName: name };
    this.fingerprintSync = fingerprint;
    return this.cached;
  }
}
