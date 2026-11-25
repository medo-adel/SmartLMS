import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { AuthService } from '../../core/services/auth.service';
import { AffiliateMe, OrderDetail, OrderRow } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Order history with invoice download, and the affiliate (referral) panel when the user has an affiliate profile. */
@Component({ selector: 'app-orders', imports: [DatePipe, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './orders.page.html', styleUrl: './orders.page.scss' })
export class OrdersPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly auth = inject(AuthService);
  readonly subs = signal<{ id: string; planName: string; price: number; currency: string; billingPeriod: string; status: string; startedAtUtc: string; currentPeriodEndUtc: string; cancelledAtUtc: string | null }[] | null>(null);
  readonly pendingCancel = signal<{ id: string; planName: string; currentPeriodEndUtc: string } | null>(null);
  readonly orders = signal<OrderRow[] | null>(null);
  readonly aff = signal<AffiliateMe | null>(null);
  readonly noAff = signal(false);
  readonly failed = signal(false);
  readonly copied = signal(false);
  ngOnInit(): void { void this.load(); if (this.auth.hasRole('Student')) { this.api.mySubscriptions().then((v) => this.subs.set(v)).catch(() => this.subs.set([])); } }
  reviewCancel(sub: { id: string; planName: string; currentPeriodEndUtc: string }): void { this.pendingCancel.set(sub); }
  async confirmCancel(): Promise<void> {
    const pc = this.pendingCancel(); if (!pc) return;
    try { await this.api.cancelSubscription(pc.id); this.pendingCancel.set(null); this.subs.set(await this.api.mySubscriptions()); } catch { /* toast */ }
  }
  async load(): Promise<void> {
    this.failed.set(false);
    const [o, a] = await Promise.allSettled([this.api.orders(), this.api.affiliate()]);
    if (o.status === 'fulfilled') this.orders.set(o.value.items); else this.failed.set(true);
    if (a.status === 'fulfilled') this.aff.set(a.value); else this.noAff.set(true); // 404 NOT_AN_AFFILIATE is normal
  }
  readonly detail = signal<OrderDetail | null>(null);
  readonly detailFor = signal('');
  /** GET /orders/{id}: the order's own detail (items, payments, invoice number). Student sees own orders; TenantAdmin any of the tenant. */
  async toggleDetail(o: OrderRow): Promise<void> {
    if (this.detailFor() === o.id) { this.detailFor.set(''); this.detail.set(null); return; }
    this.detailFor.set(o.id); this.detail.set(null);
    try { this.detail.set(await this.api.order(o.id)); } catch { this.detailFor.set(''); }
  }
  async invoice(o: OrderRow): Promise<void> {
    try { const blob = await this.api.invoiceBlob(o.id); const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = `invoice-${o.id}.pdf`; a.click(); URL.revokeObjectURL(a.href); } catch { /* toast */ }
  }
  async copy(a: AffiliateMe): Promise<void> { try { await navigator.clipboard.writeText(a.referralCode); this.copied.set(true); } catch { /* clipboard blocked */ } }
}
