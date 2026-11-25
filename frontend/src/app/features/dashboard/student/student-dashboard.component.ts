import { DashSkeletonComponent, DashErrorComponent } from '../../../shared/dash-state/dash-state.component';
import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { RingComponent } from '../../../shared/ring/ring.component';
import { IonButton } from '@ionic/angular';
import { ApiService } from '../../../core/services/api.service';
import { StudentDashboard } from '../../../core/models/models';
import { TranslatePipe } from '../../../core/pipes/translate.pipe';
import { KpiCardComponent } from '../../../shared/kpi-card/kpi-card.component';
import { RadarChartComponent } from '../../../shared/radar-chart/radar-chart.component';

@Component({
  selector: 'app-student-dashboard', imports: [RouterLink, RingComponent, DashSkeletonComponent, DashErrorComponent, DatePipe, TranslatePipe, KpiCardComponent, RadarChartComponent],
  templateUrl: './student-dashboard.component.html', styleUrl: './student-dashboard.component.scss',
})
export class StudentDashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly data = signal<StudentDashboard | null>(null);
  readonly failed = signal(false);
  readonly radar = computed(() => (this.data()?.skillRadar ?? []).map((s) => ({ label: s.skill, value: s.averagePercent })));
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> { this.failed.set(false); try { this.data.set(await this.api.studentDashboard()); } catch { this.failed.set(true); } }
}
