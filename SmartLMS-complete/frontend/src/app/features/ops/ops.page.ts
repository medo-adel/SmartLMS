import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { FinanceService, ApiKeyCreated, ApiKeyRow, AuditPage, BinView, WebhookCreated, WebhookDeliveries, WebhookRow } from '../../core/services/finance.service';

/** Ops: audit logs (TenantAdmin + SuperAdmin), recycle bin with two-tap restore, API keys with one-time key display (TenantAdmin). */
@Component({ selector: 'app-ops', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './ops.page.html', styleUrl: './ops.page.scss' })
export class OpsPage implements OnInit {
  private readonly api = inject(FinanceService);
  readonly tab = signal<'audit' | 'bin' | 'keys' | 'hooks'>('audit');
  readonly audit = signal<AuditPage | null>(null);
  readonly bin = signal<BinView | null>(null);
  readonly keys = signal<ApiKeyRow[] | null>(null);
  readonly newKey = signal<ApiKeyCreated | null>(null);
  readonly pendingRestore = signal<{ entity: string; id: string; name: string } | null>(null);
  readonly pendingRevoke = signal<{ id: string; name: string; prefix: string } | null>(null);
  readonly msg = signal('');
  fEntity = ''; fAction = ''; fFrom = '';
  binEntity = '';
  keyName = ''; keyScopes = ''; keyExpiry = '';
  readonly auditActions = ['Create', 'Update', 'Delete', 'Restore', 'Login', 'Export'];

  ngOnInit(): void { void this.loadAudit(); void this.loadBin(); void this.loadKeys(); void this.loadHooks(); }

  go(t: 'audit' | 'bin' | 'keys' | 'hooks'): void { this.msg.set(''); this.pendingRestore.set(null); this.pendingRevoke.set(null); this.newKey.set(null); this.newHook.set(null); this.pendingHookDelete.set(null); this.editHook.set(''); this.tab.set(t); }

  async loadAudit(page = 1): Promise<void> {
    try {
      this.audit.set(await this.api.auditLogs({
        entity: this.fEntity.trim() || undefined, action: this.fAction || undefined,
        from: this.fFrom ? new Date(this.fFrom).toISOString() : undefined, page,
      }));
    } catch { this.msg.set('common.error'); }
  }

  async loadBin(): Promise<void> {
    try { this.bin.set(await this.api.recycleBin(this.binEntity || undefined)); } catch { this.msg.set('common.error'); }
  }

  reviewRestore(entity: string, id: string, name: string): void { this.pendingRestore.set({ entity, id, name }); }

  async confirmRestore(): Promise<void> {
    const p = this.pendingRestore(); if (!p) return;
    try { await this.api.restore(p.entity, p.id); this.pendingRestore.set(null); this.msg.set('ops.restored'); await this.loadBin(); } catch { /* toast */ }
  }

  async loadKeys(): Promise<void> { try { this.keys.set(await this.api.apiKeys()); } catch { /* toast */ } }

  async createKey(): Promise<void> {
    const scopes = this.keyScopes.split(/[,\s]+/).map((x) => x.trim().toLowerCase()).filter(Boolean);
    if (!this.keyName.trim() || !scopes.length) { this.msg.set('ops.errKey'); return; }
    try {
      this.newKey.set(await this.api.createApiKey(this.keyName.trim(), scopes, this.keyExpiry ? new Date(this.keyExpiry).toISOString() : null));
      this.keyName = ''; this.keyScopes = ''; this.keyExpiry = ''; await this.loadKeys();
    } catch { /* toast */ }
  }

  reviewRevoke(k: ApiKeyRow): void { this.pendingRevoke.set({ id: k.id, name: k.name, prefix: k.prefix }); }

  async confirmRevoke(): Promise<void> {
    const p = this.pendingRevoke(); if (!p) return;
    try { await this.api.revokeApiKey(p.id); this.pendingRevoke.set(null); this.msg.set('ops.revoked'); await this.loadKeys(); } catch { /* toast */ }
  }

  scopesOf(k: ApiKeyRow): string { try { return (JSON.parse(k.scopesJson) as string[]).join(', '); } catch { return k.scopesJson; } }

  // ---- webhooks (TenantAdmin): endpoints, one-time secret on create, deliveries and retry --------------------------
  readonly hooks = signal<WebhookRow[] | null>(null);
  readonly newHook = signal<WebhookCreated | null>(null);
  readonly editHook = signal('');
  readonly deliveries = signal<WebhookDeliveries | null>(null);
  readonly deliveriesFor = signal('');
  readonly pendingHookDelete = signal<{ id: string; url: string } | null>(null);
  readonly eventTypes = ['order.paid', 'enrollment.created', 'payment.manual.approved', 'voucher.batch.ready', '*'];
  hookUrl = ''; hookActive = true; hookTypes: Record<string, boolean> = { 'order.paid': true, 'enrollment.created': false, 'payment.manual.approved': false, 'voucher.batch.ready': false, '*': false };
  editUrl = ''; editActive = true; editTypes: Record<string, boolean> = {};

  async loadHooks(): Promise<void> { try { this.hooks.set(await this.api.webhooks()); } catch { /* toast */ } }
  private pickedTypes(m: Record<string, boolean>): string[] { return Object.keys(m).filter((k) => m[k]); }
  async createHook(): Promise<void> {
    const types = this.pickedTypes(this.hookTypes);
    if (!this.hookUrl.trim().startsWith('https://') || !types.length) { this.msg.set('ops.errHook'); return; }
    try {
      this.newHook.set(await this.api.createWebhook({ url: this.hookUrl.trim(), eventTypes: types, isActive: this.hookActive }));
      this.hookUrl = ''; await this.loadHooks();
    } catch { /* toast */ }
  }
  startEditHook(h: WebhookRow): void {
    this.editHook.set(h.id); this.editUrl = h.url; this.editActive = h.isActive;
    this.editTypes = {}; for (const t of this.eventTypes) this.editTypes[t] = h.eventTypes.includes(t);
  }
  async saveHook(): Promise<void> {
    const id = this.editHook(); const types = this.pickedTypes(this.editTypes);
    if (!id || !this.editUrl.trim().startsWith('https://') || !types.length) { this.msg.set('ops.errHook'); return; }
    try { await this.api.updateWebhook(id, { url: this.editUrl.trim(), eventTypes: types, isActive: this.editActive }); this.editHook.set(''); this.msg.set('ops.hookSaved'); await this.loadHooks(); } catch { /* toast */ }
  }
  reviewHookDelete(h: WebhookRow): void { this.pendingHookDelete.set({ id: h.id, url: h.url }); }
  async confirmHookDelete(): Promise<void> {
    const p = this.pendingHookDelete(); if (!p) return;
    try { await this.api.deleteWebhook(p.id); this.pendingHookDelete.set(null); this.msg.set('ops.hookDeleted'); await this.loadHooks(); } catch { /* toast */ }
  }
  async toggleDeliveries(h: WebhookRow): Promise<void> {
    if (this.deliveriesFor() === h.id) { this.deliveriesFor.set(''); this.deliveries.set(null); return; }
    this.deliveriesFor.set(h.id); this.deliveries.set(null);
    try { this.deliveries.set(await this.api.webhookDeliveries(h.id)); } catch { /* toast */ }
  }
  async retryDelivery(id: string): Promise<void> {
    try { await this.api.retryDelivery(id); if (this.deliveriesFor()) this.deliveries.set(await this.api.webhookDeliveries(this.deliveriesFor())); } catch { /* toast */ }
  }
}
