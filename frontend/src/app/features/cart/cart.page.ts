import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { CartState } from '../../core/services/cart-state.service';
import { CartView, CheckoutOutcome, CouponPreview } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

export type CheckoutState = 'paid' | 'gateway' | 'manual' | 'other';

/** Only plain web links may be opened as a gateway checkout page. */
export function safeCheckoutUrl(url: string | undefined): string | null {
  if (!url) return null;
  try { const u = new URL(url); return u.protocol === 'https:' || u.protocol === 'http:' ? u.toString() : null; } catch { return null; }
}
/** Reads the typed outcome of POST /orders/checkout: paid at once, waiting on a gateway page, or waiting for a manual receipt. */
export function outcomeState(o: CheckoutOutcome): CheckoutState {
  if (o.status === 'Paid') return 'paid';
  if (safeCheckoutUrl(o.checkoutUrl)) return 'gateway';
  if (o.status === 'AwaitingManualReview') return 'manual';
  return 'other';
}

/** Cart and checkout. The server recomputes totals and coupons at checkout; the coupon check here is only a preview. */
@Component({ selector: 'app-cart', imports: [FormsModule, IonContent, IonButton, TranslatePipe], templateUrl: './cart.page.html', styleUrl: './cart.page.scss' })
export class CartPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly cartState = inject(CartState);
  readonly cart = signal<CartView | null>(null);
  readonly preview = signal<CouponPreview | null>(null);
  readonly failed = signal(false);
  readonly outcome = signal<CheckoutOutcome | null>(null);
  readonly state = signal<CheckoutState | null>(null);
  readonly checkoutError = signal(false);
  readonly receiptSent = signal(false);
  readonly busy = signal(false);
  coupon = '';
  referral = '';
  voucher = '';
  method = 'wallet';
  gateway = 'Paymob';
  manualMethod = 'VodafoneCash';
  reference = '';
  sender = '';
  receipt: File | null = null;

  ngOnInit(): void { void this.load(); }
  get url(): string | null { return safeCheckoutUrl(this.outcome()?.checkoutUrl); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { const c = await this.api.cart(); this.cart.set(c); this.cartState.apply(c); } catch { this.failed.set(true); }
  }
  async remove(id: string): Promise<void> { try { await this.api.removeFromCart(id); this.preview.set(null); await this.load(); } catch { /* toast */ } }
  async check(): Promise<void> { if (!this.coupon.trim()) return; try { this.preview.set(await this.api.previewCoupon(this.coupon.trim())); } catch { this.preview.set(null); } }
  async checkout(): Promise<void> {
    this.busy.set(true); this.checkoutError.set(false);
    try {
      const o = await this.api.checkout(this.method, this.coupon.trim() || null, this.method === 'voucher' ? this.voucher.trim() || null : null, this.method === 'gateway' ? this.gateway : null, this.referral.trim() || null);
      this.outcome.set(o); this.state.set(outcomeState(o)); this.receiptSent.set(false);
      await this.load();
    } catch { this.checkoutError.set(true); /* the global toast shows the API message; the cart stays as it was */ }
    this.busy.set(false);
  }
  pick(e: Event): void { this.receipt = (e.target as HTMLInputElement).files?.[0] ?? null; }
  async sendReceipt(): Promise<void> {
    const o = this.outcome(); if (!o || !this.receipt) return;
    this.busy.set(true);
    try {
      await this.api.submitManualPayment({ purpose: 'Order', method: this.manualMethod, amount: o.total, orderId: o.orderId, senderReference: this.reference || null, senderPhoneOrAccount: this.sender || null }, this.receipt);
      this.receiptSent.set(true); this.receipt = null;
    } catch { /* toast */ }
    this.busy.set(false);
  }
}
