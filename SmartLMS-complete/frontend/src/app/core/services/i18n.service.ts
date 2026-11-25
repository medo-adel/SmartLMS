import { Injectable, inject, signal, computed } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { EN } from '../i18n/en';
import { AR } from '../i18n/ar';
import { StorageService } from './storage.service';

export type Lang = 'en' | 'ar';
const DICTS: Record<Lang, Record<string, string>> = { en: EN, ar: AR };

@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly doc = inject(DOCUMENT);
  private readonly storage = inject(StorageService);
  readonly lang = signal<Lang>('en');
  readonly dir = computed<'ltr' | 'rtl'>(() => (this.lang() === 'ar' ? 'rtl' : 'ltr'));

  async init(fallback: string | null): Promise<void> {
    const saved = await this.storage.get('lang');
    this.apply(this.normalize(saved ?? fallback));
  }
  normalize(v: string | null | undefined): Lang { return v && v.toLowerCase().startsWith('ar') ? 'ar' : 'en'; }
  async set(lang: Lang): Promise<void> { this.apply(lang); await this.storage.set('lang', lang); }
  /** Applies a language without persisting (used for tenant default). */
  apply(lang: Lang): void {
    this.lang.set(lang);
    const el = this.doc.documentElement;
    el.setAttribute('lang', lang);
    el.setAttribute('dir', this.dir());
  }
  t(key: string): string { return DICTS[this.lang()][key] ?? EN[key] ?? key; }
}
