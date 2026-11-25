import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { CourseListItem, ReviewItem, VoucherBatch } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Tenant admin: manual payment review queue and voucher batches. Approving credits the wallet or grants the order on the server. */
@Component({ selector: 'app-payments', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './payments.page.html', styleUrl: './payments.page.scss' })
export class PaymentsPage implements OnInit {
  private readonly api = inject(LearningService);
  readonly queue = signal<ReviewItem[] | null>(null);
  readonly batches = signal<VoucherBatch[]>([]);
  readonly courses = signal<CourseListItem[]>([]);
  readonly failed = signal(false);
  readonly rejecting = signal('');
  readonly info = signal('');
  reason = '';
  status = 'Pending';
  name = ''; prefix = ''; quantity = 50; voucherType = 'WalletCredit'; courseId = ''; walletAmount: number | null = 100; expires = '';

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const [q, b, c] = await Promise.all([this.api.reviewQueue(this.status), this.api.voucherBatches(), this.api.courses('')]);
      this.queue.set(q.items); this.batches.set(b.items); this.courses.set(c.items);
    } catch { this.failed.set(true); }
  }
  async setStatus(s: string): Promise<void> { this.status = s; this.queue.set(null); await this.load(); }
  async receipt(id: string): Promise<void> {
    try { const blob = await this.api.receiptBlob(id); window.open(URL.createObjectURL(blob), '_blank', 'noopener'); } catch { /* toast */ }
  }
  async approve(id: string): Promise<void> { try { await this.api.approveManual(id); this.info.set('approved'); await this.load(); } catch { /* toast */ } }
  async reject(id: string): Promise<void> {
    if (!this.reason.trim()) return;
    try { await this.api.rejectManual(id, this.reason.trim()); this.rejecting.set(''); this.reason = ''; this.info.set('rejected'); await this.load(); } catch { /* toast */ }
  }
  async createBatch(): Promise<void> {
    if (!this.name.trim()) return;
    const course = this.voucherType === 'CourseAccess';
    try {
      await this.api.createVoucherBatch({ name: this.name.trim(), prefix: this.prefix.trim() || null, quantity: this.quantity, voucherType: this.voucherType, courseId: course ? this.courseId || null : null, walletAmount: course ? null : this.walletAmount, expiresAtUtc: this.expires ? new Date(this.expires + 'T23:59:59Z').toISOString() : null });
      this.name = ''; this.info.set('batch'); await this.load();
    } catch { /* toast */ }
  }
  async voidBatch(id: string): Promise<void> { try { await this.api.voidVoucherBatch(id); await this.load(); } catch { /* toast */ } }
  async csv(b: VoucherBatch): Promise<void> {
    try {
      const blob = await this.api.voucherCsv(b.id); const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = `${b.name}.csv`; a.click(); URL.revokeObjectURL(a.href);
    } catch { /* toast */ }
  }
}
