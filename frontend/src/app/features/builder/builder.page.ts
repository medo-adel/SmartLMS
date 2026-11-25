import { RouterLink } from '@angular/router';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { AiQuizDraft, CourseDetail, CourseStaffRow, Lesson, LessonResource, MyCourse } from '../../core/models/learning';
import { AdminService } from '../../core/services/admin.service';
import { User } from '../../core/models/models';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastService } from '../../core/services/toast.service';
import { messageFor } from '../../core/interceptors/error.interceptor';
import { I18nService } from '../../core/services/i18n.service';

/** Instructor course builder: draft courses, sections, lessons, media upload, AI indexing and the AI quiz generator (drafts only). */
@Component({ selector: 'app-builder', imports: [FormsModule, RouterLink, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './builder.page.html', styleUrl: './builder.page.scss' })
export class BuilderPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly admin = inject(AdminService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  readonly courses = signal<MyCourse[] | null>(null);
  readonly detail = signal<CourseDetail | null>(null);
  readonly draft = signal<AiQuizDraft | null>(null);
  readonly failed = signal(false);
  readonly busy = signal(false);
  /** True while a submit-for-approval request is in flight: blocks repeat clicks and double submits. */
  readonly submitting = signal(false);
  readonly info = signal('');
  /** Last action error, shown on screen. Silent catches hid real failures (wrong file type, validation, server down) behind a dead button. */
  readonly err = signal('');
  /** Uploaded attachments per lesson id. The picker clears after upload, so without this list a successful upload looked like the file was removed. */
  readonly resMap = signal<Record<string, LessonResource[]>>({});
  private showErr(e: unknown): void { this.info.set(''); this.err.set(e instanceof HttpErrorResponse ? messageFor(e, (k) => this.i18n.t(k)) : e instanceof Error && e.message ? e.message : 'request failed'); }
  title = ''; short = '';
  sectionTitle = '';
  lessonTitle = ''; lessonType = 'Video'; lessonText = ''; lessonUrl = ''; sectionId = '';
  quizLesson = ''; mcq = 5; open = 2; cards = 5;
  selected = '';
  // inline editors
  readonly editSection = signal('');
  readonly editLesson = signal('');
  readonly chapterLesson = signal('');
  readonly confirmDelete = signal('');
  secName = '';
  lesName = ''; lesText = ''; lesUrl = '';
  loadedLesson: Lesson | null = null;
  chapters: { title: string; startSeconds: number }[] = [];
  resTitle = '';
  // pricing and slides
  price = { model: 'Free', amount: 0, currency: 'EGP', days: null as number | null };
  lessonPrice: number | null = null;
  readonly slideLesson = signal('');
  readonly slideRows = signal<{ title: string; body: string; imageUrl: string }[]>([]);

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { this.courses.set(await this.api.myCourses()); } catch { this.failed.set(true); }
  }
  async open1(id: string): Promise<void> {
    this.selected = id; this.draft.set(null);
    try { const d = await this.api.course(id); this.detail.set(d); this.price = { model: d.pricingModel, amount: d.price ?? 0, currency: d.currency ?? 'EGP', days: null }; this.sectionId = d.sections[0]?.id ?? ''; void this.loadStaffAndPrereqs(id); void this.loadResources(d); } catch (e) { this.showErr(e); }
  }
  async create(): Promise<void> {
    if (!this.title.trim()) return;
    try { await this.api.createCourse(this.title.trim(), this.short.trim()); this.title = ''; this.short = ''; await this.load(); } catch (e) { this.showErr(e); }
  }
  async addSection(): Promise<void> {
    if (!this.sectionTitle.trim() || !this.selected) return;
    try { await this.api.addSection(this.selected, this.sectionTitle.trim()); this.sectionTitle = ''; await this.open1(this.selected); } catch (e) { this.showErr(e); }
  }
  async addLesson(): Promise<void> {
    if (!this.lessonTitle.trim() || !this.sectionId) return;
    try {
      await this.api.addLesson(this.sectionId, this.lessonTitle.trim(), this.lessonType, this.lessonText.trim() || null, this.lessonUrl.trim() || null);
      this.lessonTitle = ''; this.lessonText = ''; this.lessonUrl = ''; await this.open1(this.selected);
    } catch (e) { this.showErr(e); }
  }
  async upload(lessonId: string, ev: Event): Promise<void> {
    const file = (ev.target as HTMLInputElement).files?.[0]; if (!file) return;
    this.busy.set(true);
    try { await this.api.uploadLessonMedia(lessonId, file); this.info.set('✓'); this.err.set(''); } catch (e) { this.showErr(e); }
    this.busy.set(false);
  }
  async index(lessonId: string): Promise<void> { try { await this.api.queueIngest(lessonId); this.info.set('✓'); this.err.set(''); } catch (e) { this.showErr(e); } }
  async submitApproval(): Promise<void> {
    if (this.submitting() || !this.selected) return;
    this.submitting.set(true);
    try {
      await this.api.submitForApproval(this.selected);
      this.err.set('');
      await this.load(); // status refetch: the list now shows the course as pending review
      void this.toast.success(this.i18n.t('build.submitted')); // only after the server accepted
    } catch (e) { this.showErr(e); }
    finally { this.submitting.set(false); }
  }

  // ---- staff (GET/PUT /courses/{id}/staff) and prerequisites (GET/PUT /courses/{id}/prerequisites) ------------------
  readonly staff = signal<CourseStaffRow[] | null>(null);
  readonly staffDraft = signal<{ userId: string; fullName: string; staffRole: string }[]>([]);
  readonly staffMatches = signal<User[]>([]);
  staffSearch = ''; staffRole = 'TeachingAssistant';
  readonly pendingStaff = signal<{ userId: string; fullName: string; staffRole: string }[] | null>(null);
  readonly prereqs = signal<{ courseId: string; title: string }[] | null>(null);
  readonly prereqDraft = signal<string[]>([]);
  readonly prereqUnavailable = signal(false);
  readonly pendingPrereq = signal<string[] | null>(null);

  private async loadStaffAndPrereqs(id: string): Promise<void> {
    this.staff.set(null); this.staffDraft.set([]); this.staffMatches.set([]); this.staffSearch = '';
    this.prereqs.set(null); this.prereqDraft.set([]); this.prereqUnavailable.set(false);
    try {
      const st = await this.api.courseStaff(id); this.staff.set(st);
      this.staffDraft.set(st.map((r) => ({ userId: r.userId, fullName: r.fullName, staffRole: r.staffRole })));
    } catch (e) { this.showErr(e); }
    try {
      const pr = await this.api.prerequisites(id); this.prereqs.set(pr); this.prereqDraft.set(pr.map((x) => x.courseId));
    } catch (e) {
      // Added backend route: older backends 404 here; say an update is required instead of showing an empty list as fact.
      this.prereqUnavailable.set(true);
    }
  }
  async searchStaff(): Promise<void> {
    if (this.staffSearch.trim().length < 2) { this.staffMatches.set([]); return; }
    try { this.staffMatches.set((await this.admin.users(this.staffSearch.trim(), '')).items.slice(0, 8)); } catch (e) { this.showErr(e); }
  }
  addStaff(u: User): void {
    if (this.staffDraft().some((x) => x.userId === u.id)) return;
    this.staffDraft.update((l) => [...l, { userId: u.id, fullName: u.fullName, staffRole: this.staffRole }]);
  }
  dropStaff(userId: string): void { this.staffDraft.update((l) => l.filter((x) => x.userId !== userId)); }
  reviewStaff(): void { this.pendingStaff.set([...this.staffDraft()]); }
  async confirmStaff(): Promise<void> {
    const p = this.pendingStaff(); if (!p || !this.selected) return;
    try {
      await this.api.setCourseStaff(this.selected, p.map((x) => ({ userId: x.userId, staffRole: x.staffRole })));
      this.pendingStaff.set(null); this.info.set('✓'); this.err.set(''); await this.loadStaffAndPrereqs(this.selected);
    } catch (e) { this.showErr(e); }
  }
  togglePrereq(id: string): void {
    this.prereqDraft.update((l) => l.includes(id) ? l.filter((x) => x !== id) : [...l, id]);
  }
  reviewPrereq(): void { this.pendingPrereq.set([...this.prereqDraft()]); }
  prereqTitle(id: string): string { return (this.courses() ?? []).find((c) => c.id === id)?.title ?? id; }
  async confirmPrereq(): Promise<void> {
    const p = this.pendingPrereq(); if (!p || !this.selected) return;
    try {
      await this.api.setPrerequisites(this.selected, p);
      this.pendingPrereq.set(null); this.info.set('✓'); this.err.set(''); await this.loadStaffAndPrereqs(this.selected);
    } catch (e) { this.showErr(e); }
  }
  async makeQuiz(): Promise<void> {
    if (!this.quizLesson) return;
    this.busy.set(true); this.draft.set(null);
    try { this.draft.set(await this.api.generateQuiz(this.quizLesson, this.mcq, this.open, this.cards)); } catch (e) { this.showErr(e); }
    this.busy.set(false);
  }
  get lessons(): { id: string; title: string }[] { return (this.detail()?.sections ?? []).flatMap((s) => s.lessons.map((l) => ({ id: l.id, title: l.title }))); }

  private async refresh(): Promise<void> { await this.open1(this.selected); }
  startRename(id: string, title: string): void { this.editSection.set(id); this.secName = title; }
  async saveSection(id: string): Promise<void> {
    const sec = this.detail()?.sections.find((x) => x.id === id); if (!sec || !this.secName.trim()) return;
    try { await this.api.updateSection(id, this.secName.trim(), sec.description, sec.sortOrder); this.editSection.set(''); await this.refresh(); } catch (e) { this.showErr(e); }
  }
  async removeSection(id: string): Promise<void> {
    if (this.confirmDelete() !== id) { this.confirmDelete.set(id); return; }
    this.confirmDelete.set('');
    try { await this.api.deleteSection(id); await this.refresh(); } catch (e) { this.showErr(e); }
  }
  async removeLesson(id: string): Promise<void> {
    if (this.confirmDelete() !== id) { this.confirmDelete.set(id); return; }
    this.confirmDelete.set('');
    try { await this.api.deleteLesson(id); await this.refresh(); } catch (e) { this.showErr(e); }
  }
  /** New order after moving the item at index i by delta (-1 up, +1 down); null at the ends. */
  static moved(ids: string[], i: number, delta: number): string[] | null {
    const j = i + delta; if (j < 0 || j >= ids.length) return null;
    const a = [...ids]; [a[i], a[j]] = [a[j], a[i]]; return a;
  }
  async moveSection(i: number, delta: number): Promise<void> {
    const ids = BuilderPage.moved((this.detail()?.sections ?? []).map((x) => x.id), i, delta); if (!ids) return;
    try { await this.api.orderSections(this.selected, ids); await this.refresh(); } catch (e) { this.showErr(e); }
  }
  async moveLesson(sectionId: string, i: number, delta: number): Promise<void> {
    const sec = this.detail()?.sections.find((x) => x.id === sectionId); if (!sec) return;
    const ids = BuilderPage.moved(sec.lessons.map((x) => x.id), i, delta); if (!ids) return;
    try { await this.api.orderLessons(sectionId, ids); await this.refresh(); } catch (e) { this.showErr(e); }
  }
  async startEditLesson(id: string): Promise<void> {
    try {
      const l = await this.api.lesson(id); this.loadedLesson = l; this.lessonPrice = null; this.lesName = l.title; this.lesText = l.contentMarkdown ?? ''; this.lesUrl = l.externalUrl ?? ''; this.editLesson.set(id); this.chapterLesson.set('');
    } catch (e) { this.showErr(e); }
  }
  async saveLesson(sortOrder: number): Promise<void> {
    const l = this.loadedLesson; if (!l || !this.lesName.trim()) return;
    try { await this.api.updateLesson(l.id, l, this.lesName.trim(), this.lesText.trim() || null, this.lesUrl.trim() || null, sortOrder); this.editLesson.set(''); await this.refresh(); } catch (e) { this.showErr(e); }
  }
  startChapters(id: string): void { this.chapterLesson.set(id); this.editLesson.set(''); this.chapters = [{ title: '', startSeconds: 0 }]; }
  addChapter(): void { this.chapters = [...this.chapters, { title: '', startSeconds: 0 }]; }
  dropChapter(i: number): void { this.chapters = this.chapters.filter((_, k) => k !== i); }
  async saveChapters(): Promise<void> {
    const rows = this.chapters.filter((c) => c.title.trim()).map((c) => ({ title: c.title.trim(), startSeconds: Math.max(0, Math.floor(Number(c.startSeconds) || 0)) })).sort((a, b) => a.startSeconds - b.startSeconds);
    try { await this.api.setChapters(this.chapterLesson(), rows); this.info.set('✓'); this.err.set(''); this.chapterLesson.set(''); } catch (e) { this.showErr(e); }
  }
  async savePricing(): Promise<void> {
    try { await this.api.setPricing(this.selected, this.price.model, this.price.model === 'OneTime' ? Number(this.price.amount) : 0, this.price.currency || null, this.price.days ? Number(this.price.days) : null); this.info.set('✓'); this.err.set(''); await this.refresh(); } catch (e) { this.showErr(e); }
  }
  async saveLessonPrice(id: string): Promise<void> {
    try { await this.api.setLessonPrice(id, this.lessonPrice && this.lessonPrice > 0 ? Number(this.lessonPrice) : null); this.info.set('✓'); this.err.set(''); } catch (e) { this.showErr(e); }
  }
  /** Slides are plain title, text and optional image link; the API stores each slide as JSON and replaces the whole deck on save. */
  async startSlides(id: string): Promise<void> {
    this.slideLesson.set(id); this.editLesson.set(''); this.chapterLesson.set('');
    try { const rows = await this.api.slides(id); this.slideRows.set(rows.length ? rows.map((r) => ({ title: String(r.content.title ?? ''), body: String(r.content.body ?? ''), imageUrl: String(r.content.imageUrl ?? '') })) : [{ title: '', body: '', imageUrl: '' }]); } catch { this.slideRows.set([{ title: '', body: '', imageUrl: '' }]); }
  }
  addSlide(): void { this.slideRows.update((r) => [...r, { title: '', body: '', imageUrl: '' }]); }
  dropSlide(i: number): void { this.slideRows.update((r) => r.filter((_, k) => k !== i)); }
  async saveSlides(): Promise<void> {
    const deck = this.slideRows().filter((r) => r.title.trim() || r.body.trim()).map((r) => ({ title: r.title.trim(), body: r.body.trim(), ...(r.imageUrl.trim().startsWith('https://') ? { imageUrl: r.imageUrl.trim() } : {}) }));
    try { await this.api.setSlides(this.slideLesson(), deck); this.info.set('✓'); this.err.set(''); this.slideLesson.set(''); } catch (e) { this.showErr(e); }
  }
  async uploadResource(lessonId: string, ev: Event): Promise<void> {
    const input = ev.target as HTMLInputElement; const file = input.files?.[0]; if (!file) return;
    this.busy.set(true);
    try {
      await this.api.uploadResource(lessonId, file, this.resTitle.trim() || null, true);
      this.info.set('✓'); this.err.set(''); this.resTitle = ''; await this.refreshResources(lessonId);
    } catch (e) { this.showErr(e); }
    input.value = ''; this.busy.set(false);
  }
  private async loadResources(d: CourseDetail): Promise<void> {
    const entries = await Promise.all(d.sections.flatMap((s) => s.lessons.map(async (l) => {
      try { return [l.id, await this.api.resources(l.id)] as const; } catch { return [l.id, [] as LessonResource[]] as const; }
    })));
    this.resMap.set(Object.fromEntries(entries));
  }
  private async refreshResources(lessonId: string): Promise<void> {
    try { const list = await this.api.resources(lessonId); this.resMap.update((m) => ({ ...m, [lessonId]: list })); } catch { /* the upload itself succeeded; the list refresh is best-effort */ }
  }
  async downloadResource(lessonId: string, r: LessonResource): Promise<void> {
    try {
      const blob = await this.api.resourceBlob(lessonId, r.id); const a = document.createElement('a');
      a.href = URL.createObjectURL(blob); a.download = r.fileName || r.title; a.click(); URL.revokeObjectURL(a.href);
    } catch (e) { this.showErr(e); }
  }
  resSize(b: number): string { return b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`; }
}
