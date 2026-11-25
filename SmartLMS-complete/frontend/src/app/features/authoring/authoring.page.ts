import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { I18nService } from '../../core/services/i18n.service';
import { CourseDetail, ExamAttemptRow, ExamDetail, ExamListItem, GradingAnswer, GradingView, MyCourse, QuestionBank, QuestionDto, QuestionInput, QuizPointDto } from '../../core/models/learning';
import { AdminService } from '../../core/services/admin.service';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

export const QUESTION_TYPES = ['McqSingle', 'McqMulti', 'TrueFalse', 'FillBlank', 'Matching', 'ShortAnswer', 'Essay'] as const;
export interface OptionRow { text: string; isCorrect: boolean; matchKey: string; }

/** Builds the request body for a question. Choice questions need at least two options and a correct one; true/false uses the flag; free text has no options. */
export function buildQuestion(f: { type: string; text: string; points: number; difficulty: string; topic: string; explanation: string; tf: boolean; options: OptionRow[] }): QuestionInput | string {
  if (!f.text.trim()) return 'text';
  const base = { questionType: f.type, text: f.text.trim(), points: f.points > 0 ? f.points : 1, difficulty: f.difficulty || null, topic: f.topic.trim() || null, explanation: f.explanation.trim() || null };
  const opts = f.options.filter((o) => o.text.trim());
  switch (f.type) {
    case 'TrueFalse': return { ...base, trueFalseAnswer: f.tf, options: null };
    case 'ShortAnswer': case 'Essay': return { ...base, trueFalseAnswer: null, options: null };
    case 'McqSingle': if (opts.length < 2 || opts.filter((o) => o.isCorrect).length !== 1) return 'single'; break;
    case 'McqMulti': if (opts.length < 2 || !opts.some((o) => o.isCorrect)) return 'multi'; break;
    case 'FillBlank': if (!opts.some((o) => o.isCorrect)) return 'blank'; break;
    case 'Matching': if (opts.length < 2 || opts.some((o) => !o.matchKey.trim())) return 'match'; break;
  }
  return { ...base, trueFalseAnswer: null, options: opts.map((o) => ({ text: o.text.trim(), isCorrect: f.type === 'Matching' ? true : o.isCorrect, matchKey: f.type === 'Matching' ? o.matchKey.trim() : null })) };
}

/** Instructor assessment authoring: question banks and questions, exams (published here), and interactive video quiz points. */
@Component({ selector: 'app-authoring', imports: [FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './authoring.page.html', styleUrl: './authoring.page.scss' })
export class AuthoringPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly i18n = inject(I18nService);
  readonly types = QUESTION_TYPES;
  readonly courses = signal<MyCourse[] | null>(null);
  readonly detail = signal<CourseDetail | null>(null);
  readonly banks = signal<QuestionBank[]>([]);
  readonly questions = signal<QuestionDto[]>([]);
  readonly exams = signal<ExamListItem[]>([]);
  readonly points = signal<QuizPointDto[]>([]);
  readonly failed = signal(false);
  readonly msg = signal('');
  course = ''; bank = '';
  bankName = ''; bankTopic = '';
  q = { type: 'McqSingle', text: '', points: 1, difficulty: 'Medium', topic: '', explanation: '', tf: true, options: [{ text: '', isCorrect: true, matchKey: '' }, { text: '', isCorrect: false, matchKey: '' }] as OptionRow[] };
  ex = { title: '', kind: 'Quiz', minutes: 20, attempts: 1, pass: 60, pickCount: 5, instructions: '', tabs: true };
  pt = { lesson: '', question: '', at: 0, required: true, skip: 3 };

  ngOnInit(): void { void this.init(); }
  private async init(): Promise<void> { try { this.courses.set(await this.api.myCourses()); } catch { this.failed.set(true); } }
  async pickCourse(): Promise<void> {
    this.bank = ''; this.questions.set([]); this.msg.set('');
    if (!this.course) return;
    try {
      const [d, b, e] = await Promise.all([this.api.course(this.course), this.api.questionBanks(this.course), this.api.exams(this.course)]);
      this.detail.set(d); this.banks.set(b); this.exams.set(e);
    } catch { /* toast */ }
  }
  async createBank(): Promise<void> {
    if (!this.bankName.trim() || !this.course) return;
    try { await this.api.createBank(this.course, this.bankName.trim(), this.bankTopic.trim() || null); this.bankName = ''; this.bankTopic = ''; this.banks.set(await this.api.questionBanks(this.course)); } catch { /* toast */ }
  }
  async pickBank(): Promise<void> { this.questions.set([]); if (this.bank) try { this.questions.set(await this.api.questions(this.bank)); } catch { /* toast */ } }
  addOpt(): void { this.q.options = [...this.q.options, { text: '', isCorrect: false, matchKey: '' }]; }
  dropOpt(i: number): void { this.q.options = this.q.options.filter((_, k) => k !== i); }
  /** Single choice: ticking one option clears the others. */
  markCorrect(i: number): void { if (this.q.type === 'McqSingle') this.q.options = this.q.options.map((o, k) => ({ ...o, isCorrect: k === i })); }
  async saveQuestion(): Promise<void> {
    const body = buildQuestion(this.q);
    if (typeof body === 'string') { this.msg.set('au.err.' + body); return; }
    try {
      await this.api.createQuestion(this.bank, body); this.msg.set('au.saved');
      this.q = { ...this.q, text: '', explanation: '', options: [{ text: '', isCorrect: true, matchKey: '' }, { text: '', isCorrect: false, matchKey: '' }] };
      await this.pickBank(); this.banks.set(await this.api.questionBanks(this.course));
    } catch { this.msg.set(''); }
  }
  async removeQuestion(id: string): Promise<void> { try { await this.api.deleteQuestion(id); await this.pickBank(); } catch { /* toast */ } }
  async createExam(): Promise<void> {
    if (!this.ex.title.trim() || !this.bank || !this.course) { this.msg.set('au.err.exam'); return; }
    try {
      await this.api.createExam(this.course, { title: this.ex.title.trim(), kind: this.ex.kind, instructions: this.ex.instructions.trim() || null, durationMinutes: this.ex.minutes > 0 ? this.ex.minutes : null, maxAttempts: this.ex.attempts, passingScorePercent: this.ex.pass, questionIds: null, pickFromBankId: this.bank, pickCount: this.ex.pickCount, availableFromUtc: null, availableUntilUtc: null, tabSwitchDetection: this.ex.tabs, maxTabSwitches: this.ex.tabs ? 3 : null });
      this.ex.title = ''; this.msg.set('au.saved'); this.exams.set(await this.api.exams(this.course));
    } catch { this.msg.set(''); }
  }
  async publish(id: string): Promise<void> { try { await this.api.publishExam(id); this.exams.set(await this.api.exams(this.course)); this.msg.set('au.published'); } catch { /* toast */ } }

  // ---- exam edit (PUT /exams/{id}) ---------------------------------------------------------------------------------
  private readonly admin = inject(AdminService);
  readonly examDetail = signal<ExamDetail | null>(null);
  editEx = { title: '', instructions: '', minutes: 20, attempts: 1, pass: 60, from: '', until: '', tabs: true, maxTabs: 3 };
  /** Prefill from the author exam detail; UpdateExamAsync only writes settings, never question rules, and Kind is immutable. */
  async startEditExam(id: string): Promise<void> {
    this.msg.set('');
    try {
      const d = await this.api.exam(id);
      this.examDetail.set(d);
      this.editEx = { title: d.title, instructions: d.instructions ?? '', minutes: d.durationMinutes ?? 20, attempts: d.maxAttempts, pass: d.passingScorePercent, from: d.availableFromUtc ? d.availableFromUtc.slice(0, 16) : '', until: d.availableUntilUtc ? d.availableUntilUtc.slice(0, 16) : '', tabs: d.tabSwitchDetection, maxTabs: d.maxTabSwitches ?? 3 };
    } catch { /* toast */ }
  }
  async saveExam(): Promise<void> {
    const d = this.examDetail(); if (!d || !this.editEx.title.trim()) return;
    try {
      await this.api.updateExam(d.id, { title: this.editEx.title.trim(), kind: null, instructions: this.editEx.instructions.trim() || null, durationMinutes: this.editEx.minutes > 0 ? this.editEx.minutes : null, maxAttempts: this.editEx.attempts, passingScorePercent: this.editEx.pass, questionIds: null, pickFromBankId: null, pickCount: null, availableFromUtc: this.editEx.from ? new Date(this.editEx.from).toISOString() : null, availableUntilUtc: this.editEx.until ? new Date(this.editEx.until).toISOString() : null, tabSwitchDetection: this.editEx.tabs, maxTabSwitches: this.editEx.tabs ? this.editEx.maxTabs : null });
      this.examDetail.set(null); this.msg.set('au.saved'); this.exams.set(await this.api.exams(this.course));
    } catch { this.msg.set(''); }
  }

  // ---- manual grading (GET /exams/{id}/attempts, GET /attempts/{id}/result, PUT /attempt-answers/{id}/grade) --------
  readonly attemptsExam = signal('');
  readonly attempts = signal<ExamAttemptRow[] | null>(null);
  readonly attemptNames = signal<Record<string, string>>({});
  readonly grading = signal<GradingView | null>(null);
  gradeInputs: Record<string, { points: number | null; feedback: string }> = {};
  attemptLabel(r: ExamAttemptRow): string { return this.attemptNames()[r.userId] ?? r.userId; }
  async loadAttempts(examId: string): Promise<void> {
    this.attemptsExam.set(examId); this.attempts.set(null); this.grading.set(null);
    try {
      const rows = await this.api.examAttempts(examId); this.attempts.set(rows);
      for (const r of rows) {
        if (this.attemptNames()[r.userId]) continue;
        this.admin.user(r.userId).then((u) => this.attemptNames.update((m) => ({ ...m, [r.userId]: u.fullName }))).catch(() => undefined);
      }
    } catch { /* toast */ }
  }
  async openAttempt(id: string): Promise<void> {
    try {
      const v = await this.api.attemptForGrading(id); this.grading.set(v); this.gradeInputs = {};
      for (const a of v.answers ?? []) this.gradeInputs[a.answerId] = { points: a.pointsAwarded, feedback: a.feedback ?? '' };
    } catch { /* toast */ }
  }
  /** Readable rendering of the recognized answer shapes students submit ({text}, {optionId}, {optionIds}, {pairs}); anything else gets a clearly labeled raw fallback. */
  answerText(a: GradingAnswer): string {
    const j = a.answerJson as { text?: unknown; optionId?: unknown; optionIds?: unknown; pairs?: unknown } | string | null;
    if (j && typeof j === 'object') {
      if (typeof j.text === 'string') return j.text;
      if (typeof j.optionId === 'string') return `${this.i18n.t('au.selectedOption')}: ${j.optionId}`;
      if (Array.isArray(j.optionIds)) return `${this.i18n.t('au.selectedOptions')}: ${(j.optionIds as string[]).join(', ')}`;
      if (j.pairs && typeof j.pairs === 'object') return Object.entries(j.pairs as Record<string, string>).map(([l, r]) => `${l} -> ${r}`).join('\n');
      return `${this.i18n.t('au.rawAnswer')}: ${JSON.stringify(j)}`;
    }
    return typeof j === 'string' ? j : String(j ?? '');
  }
  async saveGrade(a: GradingAnswer): Promise<void> {
    const g = this.gradeInputs[a.answerId]; const attempt = this.grading();
    if (!g || !attempt || g.points === null || Number.isNaN(g.points) || g.points < 0) { this.msg.set('au.err.grade'); return; }
    try {
      await this.api.gradeAnswer(a.answerId, g.points, g.feedback.trim() || null);
      this.msg.set('au.saved'); await this.openAttempt(attempt.attemptId);
      if (this.attemptsExam()) await this.loadAttempts(this.attemptsExam());
    } catch { this.msg.set(''); }
  }

  // ---- question import (POST /question-banks/{id}/import) -----------------------------------------------------------
  imp = { format: 'json', content: '' };
  readonly impCount = signal<number | null>(null);
  async importQuestions(): Promise<void> {
    if (!this.bank || !this.imp.content.trim()) { this.msg.set('au.err.import'); return; }
    try {
      const r = await this.api.importQuestions(this.bank, this.imp.format, this.imp.content);
      this.impCount.set(r.imported); this.imp.content = ''; this.msg.set('');
      await this.pickBank(); this.banks.set(await this.api.questionBanks(this.course));
    } catch { this.impCount.set(null); }
  }
  get lessons(): { id: string; title: string }[] { return (this.detail()?.sections ?? []).flatMap((s) => s.lessons.filter((l) => l.lessonType === 'Video').map((l) => ({ id: l.id, title: l.title }))); }
  async pickLesson(): Promise<void> { this.points.set([]); if (this.pt.lesson) try { this.points.set(await this.api.quizPoints(this.pt.lesson)); } catch { /* toast */ } }
  private payload(list: QuizPointDto[]) { return list.map((p) => ({ questionId: p.question.id, timestampSeconds: p.timestampSeconds, isRequired: p.isRequired, allowSkipAfterAttempts: p.allowSkipAfterAttempts })); }
  /** The API replaces the whole list of pause points, so the current list is sent back with the change. */
  async addPoint(): Promise<void> {
    const qu = this.questions().find((x) => x.id === this.pt.question); if (!qu || !this.pt.lesson) { this.msg.set('au.err.point'); return; }
    const next = [...this.payload(this.points()), { questionId: qu.id, timestampSeconds: Math.max(0, Math.floor(this.pt.at)), isRequired: this.pt.required, allowSkipAfterAttempts: this.pt.required ? null : this.pt.skip }];
    try { await this.api.setQuizPoints(this.pt.lesson, next); this.msg.set('au.saved'); await this.pickLesson(); } catch { /* toast */ }
  }
  async removePoint(id: string): Promise<void> {
    try { await this.api.setQuizPoints(this.pt.lesson, this.payload(this.points().filter((p) => p.id !== id))); await this.pickLesson(); } catch { /* toast */ }
  }
}
