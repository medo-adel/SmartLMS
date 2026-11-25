import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { EngagementService, ChildSummary, ParentAlert, ParentChild } from '../../core/services/engagement.service';

/** Parent area: link a child with the student's one-time code, view children, per-child summary and risk alerts. */
@Component({ selector: 'app-parent', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './parent.page.html', styleUrl: './parent.page.scss' })
export class ParentPage implements OnInit {
  private readonly api = inject(EngagementService);
  readonly children = signal<ParentChild[] | null>(null);
  readonly summaries = signal<Record<string, ChildSummary>>({});
  readonly alerts = signal<ParentAlert[] | null>(null);
  readonly msg = signal('');
  readonly linkStatus = signal('');
  code = '';

  ngOnInit(): void { void this.load(); }
  private async load(): Promise<void> {
    try {
      const kids = await this.api.children();
      this.children.set(kids);
      for (const k of kids) {
        try { const s = await this.api.childSummary(k.studentId); this.summaries.update((m) => ({ ...m, [k.studentId]: s })); } catch { /* keep absent */ }
      }
      this.alerts.set(await this.api.alerts());
    } catch { this.msg.set('common.error'); }
  }

  async link(): Promise<void> {
    const code = this.code.trim(); if (!code) return;
    this.linkStatus.set('');
    try { const r = await this.api.requestLink(code); this.linkStatus.set(r.status); this.code = ''; } catch { this.linkStatus.set('invalid'); }
  }
}
