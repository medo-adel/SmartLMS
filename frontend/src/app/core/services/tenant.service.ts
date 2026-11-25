import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { Branding } from '../models/models';
import { ionColorVars, gradientEnd } from '../util/color';
import { StorageService } from './storage.service';
import { I18nService } from './i18n.service';

/** White-label: loads public branding for the tenant and writes validated CSS variables. */
@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly http = inject(HttpClient);
  private readonly doc = inject(DOCUMENT);
  private readonly storage = inject(StorageService);
  private readonly i18n = inject(I18nService);
  readonly tenantKey = signal<string>(environment.defaultTenantId);
  readonly branding = signal<Branding | null>(null);

  async init(): Promise<void> {
    const saved = await this.storage.get('tenantKey');
    // An empty saved key is meaningful: platform (SuperAdmin) context, no tenant header.
    if (saved !== null && saved !== undefined) this.tenantKey.set(saved);
    await this.loadBranding();
  }
  async setTenant(key: string): Promise<void> {
    this.tenantKey.set(key.trim());
    await this.storage.set('tenantKey', key.trim());
    await this.loadBranding();
  }
  /** Token tenant id wins after login; keep the header consistent with it. */
  adoptTenantId(id: string | null): void { if (id) { this.tenantKey.set(id); void this.storage.set('tenantKey', id); } }

  async loadBranding(): Promise<void> {
    if (!this.tenantKey()) { this.branding.set(null); return; }
    try {
      const res = await firstValueFrom(this.http.get<ApiResponse<Branding>>(`${environment.apiBaseUrl}/tenants/current/branding`));
      if (res.success && res.data) { this.branding.set(res.data); this.applyTheme(res.data); }
    } catch { /* keep defaults; login still works with the stock theme */ }
  }

  applyTheme(b: Branding): void {
    const style = this.doc.documentElement.style;
    const vars: Record<string, string> = {
      ...ionColorVars('primary', b.primaryColor), ...ionColorVars('secondary', b.secondaryColor), ...ionColorVars('tertiary', b.accentColor),
    };
    for (const [k, v] of Object.entries(vars)) style.setProperty(k, v);
    const end = gradientEnd(b.primaryColor, b.accentColor);
    if (end) style.setProperty('--brand-end', end);
    this.doc.title = b.name;
    void this.storage.get('lang').then((l) => { if (!l) this.i18n.apply(this.i18n.normalize(b.defaultLanguage)); });
  }
}
