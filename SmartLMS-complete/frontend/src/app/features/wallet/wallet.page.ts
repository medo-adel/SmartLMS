import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { ManualPayment, WalletBalance, WalletTx } from '../../core/models/learning';
import { safeCheckoutUrl } from '../cart/cart.page';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

@Component({ selector: 'app-wallet', imports: [DatePipe, FormsModule, RouterLink, IonContent, IonButton, TranslatePipe], templateUrl: './wallet.page.html', styleUrl: './wallet.page.scss' })
export class WalletPage implements OnInit {
  private readonly api = inject(LearningService);
  readonly balance = signal<WalletBalance | null>(null);
  readonly txs = signal<WalletTx[]>([]);
  readonly mine = signal<ManualPayment[]>([]);
  readonly failed = signal(false);
  readonly note = signal('');
  code = '';
  courseCode = '';
  readonly courseRedeemed = signal('');
  method = 'VodafoneCash';
  amount: number | null = null;
  reference = '';
  sender = '';
  receipt: File | null = null;
  topUpAmount: number | null = null;
  gateway = 'Paymob';
  readonly checkoutUrl = signal<string | null>(null);

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const [b, t, m] = await Promise.all([this.api.wallet(), this.api.walletTransactions(), this.api.myManualPayments()]);
      this.balance.set(b); this.txs.set(t.items); this.mine.set(m);
    } catch { this.failed.set(true); }
  }
  async redeemCourse(): Promise<void> {
    if (!this.courseCode.trim()) return;
    try { const r = await this.api.redeemCourseVoucher(this.courseCode.trim()); this.courseRedeemed.set(r.title); this.courseCode = ''; } catch { this.courseRedeemed.set(''); }
  }
  async redeem(): Promise<void> {
    if (!this.code.trim()) return;
    try { await this.api.redeemVoucherToWallet(this.code.trim()); this.code = ''; this.note.set('ok'); await this.load(); } catch { this.note.set(''); }
  }
  async submitManual(): Promise<void> {
    if (!this.amount || this.amount <= 0 || !this.receipt) return;
    try {
      await this.api.submitManualPayment({ purpose: 'WalletTopUp', method: this.method, amount: this.amount, orderId: null, senderReference: this.reference || null, senderPhoneOrAccount: this.sender || null }, this.receipt);
      this.amount = null; this.reference = ''; this.sender = ''; this.receipt = null; this.note.set('pending'); await this.load();
    } catch { this.note.set(''); }
  }
  pick(e: Event): void { this.receipt = (e.target as HTMLInputElement).files?.[0] ?? null; }
  /** Starts a gateway top-up. The wallet is credited by the gateway webhook on the server, not by this screen. */
  async topUp(): Promise<void> {
    if (!this.topUpAmount || this.topUpAmount <= 0) return;
    try { const r = await this.api.topUpGateway(this.topUpAmount, this.gateway); this.checkoutUrl.set(safeCheckoutUrl(r.checkoutUrl)); this.note.set('gateway'); } catch { this.checkoutUrl.set(null); }
  }
}
