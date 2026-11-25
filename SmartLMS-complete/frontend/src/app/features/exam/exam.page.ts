import { Component, HostListener, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { AnswerValue, AttemptQuestion, AttemptResult, AttemptView } from '../../core/models/learning';
import { PrivacyService } from '../../core/services/privacy.service';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Exam runner: starts or resumes an attempt, autosaves answers, reports tab switches, submits. The server grades and owns the clock. */
@Component({ selector: 'app-exam', imports: [RouterLink, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './exam.page.html', styleUrl: './exam.page.scss' })
export class ExamPage implements OnInit, OnDestroy {
  private readonly api = inject(LearningService);
  private readonly route = inject(ActivatedRoute);
  private readonly privacy = inject(PrivacyService);
  readonly attempt = signal<AttemptView | null>(null);
  readonly answers = signal<Record<string, AnswerValue>>({});
  readonly left = signal<number | null>(null);
  readonly result = signal<AttemptResult | null>(null);
  readonly failed = signal(false);
  readonly saved = signal(false);
  readonly busy = signal(false);
  private tick: ReturnType<typeof setInterval> | null = null;
  private autosave: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void { void this.privacy.enable(); void this.start(); }
  ngOnDestroy(): void { void this.privacy.disable(); if (this.tick) clearInterval(this.tick); if (this.autosave) clearInterval(this.autosave); }

  async start(): Promise<void> {
    this.failed.set(false);
    try {
      const { attemptId } = await this.api.startAttempt(this.route.snapshot.paramMap.get('examId') ?? '');
      const a = await this.api.attempt(attemptId);
      this.attempt.set(a);
      this.answers.set(Object.fromEntries(a.questions.filter((q) => q.savedAnswer).map((q) => [q.attemptQuestionId, q.savedAnswer as AnswerValue])));
      this.left.set(a.remainingSeconds);
      this.tick = setInterval(() => {
        const l = this.left(); if (l === null) return;
        if (l <= 1) { this.left.set(0); void this.submit(); } else this.left.set(l - 1);
      }, 1000);
      this.autosave = setInterval(() => void this.save(), 20000);
    } catch { this.failed.set(true); }
  }
  setSingle(q: AttemptQuestion, optionId: string): void { this.answers.update((a) => ({ ...a, [q.attemptQuestionId]: { optionId } })); }
  toggle(q: AttemptQuestion, optionId: string): void {
    this.answers.update((a) => {
      const cur = a[q.attemptQuestionId]?.optionIds ?? [];
      return { ...a, [q.attemptQuestionId]: { optionIds: cur.includes(optionId) ? cur.filter((x) => x !== optionId) : [...cur, optionId] } };
    });
  }
  setText(q: AttemptQuestion, text: string): void { this.answers.update((a) => ({ ...a, [q.attemptQuestionId]: { text } })); }
  setPair(q: AttemptQuestion, optionId: string, target: string): void {
    this.answers.update((a) => ({ ...a, [q.attemptQuestionId]: { pairs: { ...(a[q.attemptQuestionId]?.pairs ?? {}), [optionId]: target } } }));
  }
  isChecked(q: AttemptQuestion, id: string): boolean { const v = this.answers()[q.attemptQuestionId]; return v?.optionId === id || (v?.optionIds ?? []).includes(id); }
  isMulti(q: AttemptQuestion): boolean { return q.type === 'McqMulti'; }
  isText(q: AttemptQuestion): boolean { return ['FillBlank', 'ShortAnswer', 'Essay'].includes(q.type); }
  pair(q: AttemptQuestion, id: string): string { return this.answers()[q.attemptQuestionId]?.pairs?.[id] ?? ''; }
  text(q: AttemptQuestion): string { return this.answers()[q.attemptQuestionId]?.text ?? ''; }

  async save(): Promise<void> {
    const a = this.attempt(); if (!a || this.result()) return;
    const body = Object.entries(this.answers()).map(([attemptQuestionId, answer]) => ({ attemptQuestionId, answer }));
    try { await this.api.saveAnswers(a.attemptId, body); this.saved.set(true); setTimeout(() => this.saved.set(false), 2000); } catch { /* retried by the autosave timer */ }
  }
  async submit(): Promise<void> {
    const a = this.attempt(); if (!a || this.busy() || this.result()) return;
    this.busy.set(true);
    try {
      await this.save();
      this.result.set(await this.api.submitAttempt(a.attemptId));
      if (this.tick) clearInterval(this.tick);
    } catch { /* toast from the interceptor */ }
    this.busy.set(false);
  }
  clock(): string { const l = this.left() ?? 0; return `${Math.floor(l / 60)}:${String(l % 60).padStart(2, '0')}`; }

  private report(type: string): void { const a = this.attempt(); if (a && !this.result()) void this.api.attemptEvent(a.attemptId, type).catch(() => undefined); }
  @HostListener('document:visibilitychange') onVis(): void { if (document.hidden) this.report('TabSwitch'); }
  @HostListener('window:blur') onBlur(): void { if (!document.hidden) this.report('WindowBlur'); }
}
