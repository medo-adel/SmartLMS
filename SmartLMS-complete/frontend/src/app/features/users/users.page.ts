import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { AdminService } from '../../core/services/admin.service';
import { User, UserDevice } from '../../core/models/models';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

const CREATABLE = ['Instructor', 'TeachingAssistant', 'Student', 'Parent', 'TenantAdmin'];

/** TenantAdmin: search and manage academy users (create, edit, roles, deactivate, device reset). The server blocks the SuperAdmin role. */
@Component({ selector: 'app-users', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './users.page.html', styleUrl: './users.page.scss' })
export class UsersPage implements OnInit {
  private readonly api = inject(AdminService);
  readonly items = signal<User[] | null>(null);
  readonly total = signal(0);
  readonly failed = signal(false);
  readonly info = signal('');
  readonly creating = signal(false);
  readonly editing = signal('');
  readonly confirmDeactivate = signal('');
  readonly devicesFor = signal('');
  readonly devices = signal<UserDevice[]>([]);
  readonly rolesList = CREATABLE;
  readonly roleFilters = ['', 'TenantAdmin', 'Instructor', 'TeachingAssistant', 'Student', 'Parent'];

  search = ''; role = ''; page = 1;
  fullName = ''; email = ''; password = ''; phone = ''; preferredLanguage = 'ar'; roles: Record<string, boolean> = { Student: true };
  edit: Record<string, unknown> = {};
  editRoles: Record<string, boolean> = {};

  get pages(): number { return Math.max(1, Math.ceil(this.total() / 20)); }
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { const r = await this.api.users(this.search, this.role, this.page); this.items.set(r.items); this.total.set(r.totalCount); }
    catch { this.failed.set(true); }
  }
  async find(): Promise<void> { this.page = 1; this.items.set(null); await this.load(); }

  picked(): string[] { return CREATABLE.filter((r) => this.roles[r]); }
  toggleRole(r: string): void { this.roles[r] = !this.roles[r]; }

  async create(): Promise<void> {
    this.info.set('');
    if (!this.fullName.trim() || !this.email.trim() || this.password.length < 10 || this.picked().length === 0) return;
    try {
      await this.api.createUser({ fullName: this.fullName.trim(), email: this.email.trim(), password: this.password, roles: this.picked(), phoneNumber: this.phone.trim() || null, preferredLanguage: this.preferredLanguage });
      this.fullName = this.email = this.password = this.phone = ''; this.roles = { Student: true };
      this.creating.set(false); this.info.set('created'); await this.load();
    } catch { /* toast by interceptor */ }
  }

  startEdit(u: User): void {
    this.editing.set(u.id);
    this.edit = { fullName: u.fullName, phoneNumber: u.phoneNumber ?? '', preferredLanguage: u.preferredLanguage || 'ar' };
    this.editRoles = Object.fromEntries(CREATABLE.map((r) => [r, u.roles.includes(r)]));
  }
  pickedEdit(): string[] { return CREATABLE.filter((r) => this.editRoles[r]); }
  async saveEdit(id: string): Promise<void> {
    this.info.set('');
    try {
      await this.api.updateUser(id, { fullName: String(this.edit['fullName'] ?? '').trim(), phoneNumber: String(this.edit['phoneNumber'] ?? '').trim() || null, preferredLanguage: String(this.edit['preferredLanguage'] ?? 'ar') });
      if (this.pickedEdit().length) await this.api.setUserRoles(id, this.pickedEdit());
      this.editing.set(''); this.info.set('saved'); await this.load();
    } catch { /* toast by interceptor */ }
  }

  /** Two-tap confirmation before deactivating an account. */
  async deactivate(id: string): Promise<void> {
    if (this.confirmDeactivate() !== id) { this.confirmDeactivate.set(id); return; }
    this.confirmDeactivate.set(''); this.info.set('');
    try { await this.api.deactivateUser(id); this.info.set('deactivated'); await this.load(); } catch { /* toast by interceptor */ }
  }

  async showDevices(id: string): Promise<void> {
    if (this.devicesFor() === id) { this.devicesFor.set(''); return; }
    try { this.devices.set(await this.api.userDevices(id)); this.devicesFor.set(id); } catch { /* toast by interceptor */ }
  }
  async resetDevices(id: string): Promise<void> {
    this.info.set('');
    try { await this.api.resetUserDevices(id, null); this.info.set('devicesReset'); await this.showDevicesRefresh(id); } catch { /* toast by interceptor */ }
  }
  private async showDevicesRefresh(id: string): Promise<void> { this.devices.set(await this.api.userDevices(id)); }
}
