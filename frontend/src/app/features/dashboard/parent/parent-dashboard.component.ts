import { DashSkeletonComponent, DashErrorComponent } from '../../../shared/dash-state/dash-state.component';
import { Component, inject, signal, OnInit } from '@angular/core';
import { IonSegment, IonSegmentButton, IonLabel } from '@ionic/angular';
import { ApiService } from '../../../core/services/api.service';
import { ParentChild, ParentSummary } from '../../../core/models/models';
import { TranslatePipe } from '../../../core/pipes/translate.pipe';
import { KpiCardComponent } from '../../../shared/kpi-card/kpi-card.component';

@Component({
  selector: 'app-parent-dashboard', imports: [DashSkeletonComponent, DashErrorComponent, IonSegment, IonSegmentButton, IonLabel, TranslatePipe, KpiCardComponent],
  templateUrl: './parent-dashboard.component.html', styleUrl: './parent-dashboard.component.scss',
})
export class ParentDashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly children = signal<ParentChild[] | null>(null);
  readonly selected = signal<string | null>(null);
  readonly summary = signal<ParentSummary | null>(null);
  readonly failed = signal(false);
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { const c = await this.api.parentChildren(); this.children.set(c); if (c.length) await this.select(c[0].studentId); } catch { this.failed.set(true); }
  }
  async select(id: unknown): Promise<void> {
    if (typeof id !== 'string') return;
    this.selected.set(id); this.summary.set(null);
    try { this.summary.set(await this.api.parentSummary(id)); } catch { this.failed.set(true); }
  }
}
