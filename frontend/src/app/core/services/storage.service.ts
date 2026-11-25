import { Injectable } from '@angular/core';
import { Preferences } from '@capacitor/preferences';

/** Thin wrapper over Capacitor Preferences (localStorage on web, native storage on device). */
@Injectable({ providedIn: 'root' })
export class StorageService {
  async get(key: string): Promise<string | null> { return (await Preferences.get({ key })).value; }
  async set(key: string, value: string): Promise<void> { await Preferences.set({ key, value }); }
  async remove(key: string): Promise<void> { await Preferences.remove({ key }); }
  async getJson<T>(key: string): Promise<T | null> {
    const raw = await this.get(key);
    if (!raw) return null;
    try { return JSON.parse(raw) as T; } catch { return null; }
  }
  setJson(key: string, value: unknown): Promise<void> { return this.set(key, JSON.stringify(value)); }
}
