import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { AdminService } from '../../core/services/admin.service';
import { TenantService } from '../../core/services/tenant.service';
import { Tenant } from '../../core/models/models';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** SuperAdmin: academies (tenants) list, create (with the seeded academy admin), edit, and time-boxed support access. */
@Component({ selector: 'app-tenants', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './tenants.page.html', styleUrl: './tenants.page.scss' })
export class TenantsPage implements OnInit {
  private readonly api = inject(AdminService);
  readonly tenant = inject(TenantService);
  readonly items = signal<Tenant[] | null>(null);
  readonly failed = signal(false);
  readonly info = signal('');
  readonly editing = signal<string>('');
  readonly supportFor = signal<string>('');
  readonly granted = signal<Record<string, string>>({});

  name = ''; slug = ''; subdomain = ''; adminFullName = ''; adminEmail = ''; adminPassword = '';
  maxMobile = 1; maxWeb = 1; defaultLanguage = 'ar'; timeZoneId = 'Africa/Cairo'; currency = 'EGP';
  edit: Record<string, unknown> = {};
  minutes: number | null = 60;

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { this.items.set((await this.api.tenants()).items); } catch { this.failed.set(true); }
  }

  async create(): Promise<void> {
    this.info.set('');
    if (!this.name.trim() || !this.slug.trim() || !this.adminFullName.trim() || !this.adminEmail.trim() || this.adminPassword.length < 10) return;
    try {
      await this.api.createTenant({
        name: this.name.trim(), slug: this.slug.trim(), subdomain: this.subdomain.trim() || null,
        adminFullName: this.adminFullName.trim(), adminEmail: this.adminEmail.trim(), adminPassword: this.adminPassword,
        maxMobileDevices: this.maxMobile, maxWebDevices: this.maxWeb,
        defaultLanguage: this.defaultLanguage, timeZoneId: this.timeZoneId.trim() || 'Africa/Cairo', currency: this.currency.trim() || 'EGP',
      });
      this.name = this.slug = this.subdomain = this.adminFullName = this.adminEmail = this.adminPassword = '';
      this.info.set('created'); await this.load();
    } catch { /* toast by interceptor */ }
  }

  startEdit(t: Tenant): void {
    this.editing.set(t.id);
    this.edit = { name: t.name, subdomain: t.subdomain ?? '', isActive: t.isActive, maxMobileDevices: t.maxMobileDevices, maxWebDevices: t.maxWebDevices, defaultLanguage: t.defaultLanguage, timeZoneId: t.timeZoneId, currency: t.currency };
  }
  async saveEdit(id: string): Promise<void> {
    this.info.set('');
    try {
      await this.api.updateTenant(id, {
        name: String(this.edit['name'] ?? '').trim(), subdomain: String(this.edit['subdomain'] ?? '').trim() || null,
        isActive: Boolean(this.edit['isActive']), maxMobileDevices: Number(this.edit['maxMobileDevices']) || 1, maxWebDevices: Number(this.edit['maxWebDevices']) || 1,
        defaultLanguage: String(this.edit['defaultLanguage'] ?? 'en'), timeZoneId: String(this.edit['timeZoneId'] ?? 'Africa/Cairo').trim(), currency: String(this.edit['currency'] ?? 'EGP').trim(),
      });
      this.editing.set(''); this.info.set('saved'); await this.load();
    } catch { /* toast by interceptor */ }
  }

  /** Enter an academy's context: an explicit, time-boxed support-access grant for THIS academy (the button sits on its row), then tenant calls carry its key. */
  async enter(t: Tenant): Promise<void> {
    this.info.set('');
    try {
      const r = await this.api.grantSupport(t.id, this.minutes);
      this.granted.update((g) => ({ ...g, [t.id]: r.expiresAtUtc }));
      await this.tenant.setTenant(t.slug);
      this.info.set('entered');
    } catch { /* toast by interceptor */ }
  }
  /** Leave the academy context: back to platform context (no X-Tenant-Id). The backend grant itself stays time-boxed. */
  async exitContext(): Promise<void> { this.info.set(''); await this.tenant.setTenant(''); this.info.set('exited'); }

  async grant(id: string): Promise<void> {
    this.info.set('');
    try {
      const r = await this.api.grantSupport(id, this.minutes);
      this.granted.update((g) => ({ ...g, [id]: r.expiresAtUtc }));
      this.supportFor.set(''); this.info.set('granted');
    } catch { /* toast by interceptor */ }
  }
  readonly confirmRevoke = signal('');
  /** Two-tap confirmation before revoking support access to an academy. */
  async revoke(id: string): Promise<void> {
    if (this.confirmRevoke() !== id) { this.confirmRevoke.set(id); return; }
    this.confirmRevoke.set(''); this.info.set('');
    try {
      await this.api.revokeSupport(id);
      this.granted.update((g) => { const n = { ...g }; delete n[id]; return n; });
      this.info.set('revoked');
    } catch { /* toast by interceptor */ }
  }
}
