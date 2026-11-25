import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { I18nService } from '../../core/services/i18n.service';
import { AiMindMap, AiSearchHit, AiSummary, AiTutorResult } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { MindMapComponent } from './mind-map.component';

interface Turn { q: string; a?: AiTutorResult; error?: boolean; }

/** AI Tutor drawer: chat with citations, semantic video search, lesson summary and mind map. All calls go through the .NET API. */
@Component({ selector: 'app-ai-drawer', imports: [FormsModule, IonButton, IonSpinner, TranslatePipe, MindMapComponent], templateUrl: './ai-tutor-drawer.component.html', styleUrl: './ai-tutor-drawer.component.scss' })
export class AiTutorDrawerComponent {
  private readonly api = inject(LearningService);
  private readonly i18n = inject(I18nService);
  readonly courseId = input.required<string>();
  readonly lessonId = input.required<string>();
  readonly closed = output<void>();
  readonly seek = output<number>();
  readonly tab = signal<'chat' | 'search' | 'summary' | 'map'>('chat');
  readonly busy = signal(false);
  readonly turns = signal<Turn[]>([]);
  readonly hits = signal<AiSearchHit[] | null>(null);
  readonly summary = signal<AiSummary | null>(null);
  readonly map = signal<AiMindMap | null>(null);
  readonly failed = signal(false);
  question = '';
  query = '';

  async ask(): Promise<void> {
    const q = this.question.trim(); if (!q || this.busy()) return;
    this.question = ''; this.busy.set(true);
    this.turns.update((t) => [...t, { q }]);
    try {
      const a = await this.api.tutor(this.courseId(), q, this.i18n.lang());
      this.turns.update((t) => t.map((x, i) => (i === t.length - 1 ? { ...x, a } : x)));
    } catch { this.turns.update((t) => t.map((x, i) => (i === t.length - 1 ? { ...x, error: true } : x))); }
    this.busy.set(false);
  }
  async search(): Promise<void> {
    if (!this.query.trim()) return;
    this.busy.set(true); this.failed.set(false);
    try { this.hits.set((await this.api.semanticSearch(this.courseId(), this.query)).hits); } catch { this.failed.set(true); }
    this.busy.set(false);
  }
  async openTab(t: 'chat' | 'search' | 'summary' | 'map'): Promise<void> {
    this.tab.set(t); this.failed.set(false);
    try {
      if (t === 'summary' && !this.summary()) { this.busy.set(true); this.summary.set(await this.api.summarize(this.lessonId())); }
      if (t === 'map' && !this.map()) { this.busy.set(true); this.map.set(await this.api.mindMap(this.lessonId())); }
    } catch { this.failed.set(true); }
    this.busy.set(false);
  }
  time(s: number): string { const m = Math.floor(s / 60), r = Math.floor(s % 60); return `${m}:${String(r).padStart(2, '0')}`; }
}
