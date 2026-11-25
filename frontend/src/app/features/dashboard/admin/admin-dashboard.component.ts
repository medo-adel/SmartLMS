import { DashSkeletonComponent, DashErrorComponent } from '../../../shared/dash-state/dash-state.component';
import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { IonButton, IonSelect, IonSelectOption } from '@ionic/angular';
import { ApiService } from '../../../core/services/api.service';
import { AdminDashboard } from '../../../core/models/models';
import { TranslatePipe } from '../../../core/pipes/translate.pipe';
import { KpiCardComponent } from '../../../shared/kpi-card/kpi-card.component';
import { BarChartComponent } from '../../../shared/bar-chart/bar-chart.component';
import { ToastService } from '../../../core/services/toast.service';

export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = filename; a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

@Component({
  selector: 'app-admin-dashboard', imports: [DashSkeletonComponent, DashErrorComponent, DecimalPipe, IonButton, IonSelect, IonSelectOption, TranslatePipe, KpiCardComponent, BarChartComponent],
  templateUrl: './admin-dashboard.component.html', styleUrl: './admin-dashboard.component.scss',
})
export class AdminDashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  readonly data = signal<AdminDashboard | null>(null);
  readonly failed = signal(false);
  readonly kind = signal('revenue');
  readonly kinds = ['revenue', 'orders', 'enrollments'];
  readonly waiting = computed(() => { const q = this.data()?.queues; return q ? q.coursesAwaitingApproval + q.manualPaymentsToReview + q.payoutsToApprove + q.failedWebhookDeliveries : 0; });
  readonly daily = computed(() => (this.data()?.revenueByDay ?? []).map((r) => ({ label: r.day.slice(5, 10), value: r.revenue })));
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> { this.failed.set(false); try { this.data.set(await this.api.adminDashboard()); } catch { this.failed.set(true); } }
  async export(format: 'csv' | 'xlsx'): Promise<void> {
    try { downloadBlob(await this.api.exportReport(this.kind(), format), `${this.kind()}.${format}`); } catch { /* error toast shown by the interceptor */ }
  }
}
