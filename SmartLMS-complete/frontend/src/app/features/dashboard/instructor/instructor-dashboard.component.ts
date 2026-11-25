import { DashSkeletonComponent, DashErrorComponent } from '../../../shared/dash-state/dash-state.component';
import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { IonButton } from '@ionic/angular';
import { ApiService } from '../../../core/services/api.service';
import { InstructorDashboard } from '../../../core/models/models';
import { TranslatePipe } from '../../../core/pipes/translate.pipe';
import { KpiCardComponent } from '../../../shared/kpi-card/kpi-card.component';
import { BarChartComponent } from '../../../shared/bar-chart/bar-chart.component';

/** Heat colour for a drop-off percentage: 0 = calm, 100 = hot. Exposed for tests. */
export function heat(percent: number): number { return Math.max(0, Math.min(100, percent)) / 100; }

@Component({
  selector: 'app-instructor-dashboard', imports: [DashSkeletonComponent, DashErrorComponent, DecimalPipe, TranslatePipe, KpiCardComponent, BarChartComponent],
  templateUrl: './instructor-dashboard.component.html', styleUrl: './instructor-dashboard.component.scss',
})
export class InstructorDashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly data = signal<InstructorDashboard | null>(null);
  readonly failed = signal(false);
  readonly trend = computed(() => (this.data()?.enrollmentTrend ?? []).map((t) => ({ label: t.weekStart.slice(5, 10), value: t.enrollments })));
  readonly heat = heat;
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> { this.failed.set(false); try { this.data.set(await this.api.instructorDashboard()); } catch { this.failed.set(true); } }
}
