import { Component, ElementRef, HostListener, OnDestroy, OnInit, inject, signal, viewChild } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { AuthService } from '../../core/services/auth.service';
import { CourseDetail, CourseProgress, InteractiveQuiz, Lesson, LessonOutline, LessonResource, TranscriptCue } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { AiTutorDrawerComponent } from '../ai/ai-tutor-drawer.component';
import { LessonExtrasComponent } from './lesson-extras.component';
import { PrivacyService } from '../../core/services/privacy.service';
import { LessonSlide } from '../../core/models/learning';
import { OfflineEntry, OfflineService } from '../../core/services/offline.service';
import { ProgressSyncService } from '../../core/services/progress-sync.service';
import { NetworkService } from '../../core/services/network.service';

/**
 * Lesson player. Media is fetched from the protected stream endpoint with the bearer token and played from a blob URL.
 * Dynamic watermark and privacy screen are web-page level deterrents only (see the on-screen note).
 */
@Component({ selector: 'app-player', imports: [RouterLink, IonContent, IonButton, IonSpinner, TranslatePipe, AiTutorDrawerComponent, LessonExtrasComponent], templateUrl: './player.page.html', styleUrl: './player.page.scss' })
export class PlayerPage implements OnInit, OnDestroy {
  private readonly api = inject(LearningService);
  private readonly route = inject(ActivatedRoute);
  readonly auth = inject(AuthService);
  private readonly privacy = inject(PrivacyService);
  readonly offline = inject(OfflineService);
  private readonly sync = inject(ProgressSyncService);
  readonly net = inject(NetworkService);
  readonly offlineMode = signal(false);
  readonly dlMsg = signal('');
  readonly slides = signal<LessonSlide[]>([]);
  readonly slideIx = signal(0);
  readonly unlockMsg = signal('');
  readonly canUnlock = signal(false);
  readonly nowFn = (): number => this.media()?.nativeElement.currentTime ?? 0;
  private readonly sanitizer = inject(DomSanitizer);
  readonly safeUrl = signal<SafeResourceUrl | null>(null);
  readonly media = viewChild<ElementRef<HTMLMediaElement>>('media');
  readonly lesson = signal<Lesson | null>(null);
  readonly course = signal<CourseDetail | null>(null);
  readonly mediaUrl = signal<string | null>(null);
  readonly failed = signal(false);
  readonly locked = signal(false);
  readonly hidden = signal(false);
  readonly drawer = signal(false);
  readonly wm = signal({ x: 10, y: 10, text: '' });
  readonly quiz = signal<InteractiveQuiz | null>(null);
  /** Timed transcript: captions overlay (CC) and the seek-on-click transcript panel. */
  readonly cues = signal<TranscriptCue[]>([]);
  readonly ccOn = signal(false);
  readonly transcriptOpen = signal(false);
  readonly now = signal(0);
  readonly quizMsg = signal('');
  /** Curriculum panel (course map): progress per lesson, downloadable files, manual lesson choice. */
  readonly progress = signal<CourseProgress | null>(null);
  readonly resMap = signal<Record<string, LessonResource[]>>({});
  readonly curriculumOpen = signal(true);
  /** Set when the lesson ends or is marked complete: shows the learner-controlled "up next" card. No automatic navigation. */
  readonly showUpNext = signal(false);
  speed = 1;
  private quizzes: InteractiveQuiz[] = [];
  private answered = new Set<string>();
  private timer: ReturnType<typeof setInterval> | null = null;
  private wmTimer: ReturnType<typeof setInterval> | null = null;
  private loadSeq = 0; // bumped on every load: stale async responses of an older lesson are dropped
  private paramSub?: Subscription;
  private lastSaved = 0;
  private url: string | null = null;

  get courseId(): string { return this.route.snapshot.paramMap.get('courseId') ?? ''; }
  get lessonId(): string { return this.route.snapshot.paramMap.get('lessonId') ?? ''; }

  private studyStarted = 0;

  ngOnInit(): void {
    this.curriculumOpen.set(typeof matchMedia === 'function' ? matchMedia('(min-width: 992px)').matches : true); // desktop: map open; phone: toggled
    this.studyStarted = Date.now();
    void this.privacy.enable(); // native screenshot and recording protection while a lesson is open
    void this.offline.load().then(() => this.load());
    this.moveWatermark();
    this.wmTimer = setInterval(() => this.moveWatermark(), 7000); // the mark keeps moving so it cannot be cropped out of one corner
    // Navigating to another lesson reuses this component: reload for the new lesson instead of showing the old one.
    this.paramSub = this.route.paramMap.subscribe((m) => {
      const id = m.get('lessonId');
      if (this.lesson() && id && id !== this.lesson()!.id) { void this.flush(false); this.resetLessonState(); this.studyStarted = Date.now(); void this.load(); }
    });
  }
  private resetLessonState(): void {
    ++this.loadSeq; // invalidate anything still in flight for the previous lesson
    if (this.url) { URL.revokeObjectURL(this.url); this.url = null; }
    this.mediaUrl.set(null); this.safeUrl.set(null); this.lesson.set(null);
    this.slides.set([]); this.slideIx.set(0); this.quizzes = []; this.answered.clear(); this.quiz.set(null);
    this.cues.set([]); this.ccOn.set(false); this.transcriptOpen.set(false); this.now.set(0);
    this.showUpNext.set(false); this.lastSaved = 0; this.dlMsg.set('');
  }
  ngOnDestroy(): void {
    this.paramSub?.unsubscribe();
    if (this.timer) clearInterval(this.timer);
    if (this.wmTimer) clearInterval(this.wmTimer);
    if (this.url) URL.revokeObjectURL(this.url);
    void this.flush(false);
    void this.privacy.disable();
    // Record the study session (student only; the API caps duration at 4 h and rejects stale starts). Fire-and-forget: never block leaving the page.
    const secs = Math.round((Date.now() - this.studyStarted) / 1000);
    if (secs >= 30 && this.auth.hasRole('Student') && !this.offlineMode()) {
      void this.api.recordStudySession({ courseId: this.courseId, lessonId: this.lessonId, startedAtUtc: new Date(this.studyStarted).toISOString(), durationSeconds: Math.min(secs, 4 * 3600) }).catch(() => undefined);
    }
  }
  private moveWatermark(): void {
    const u = this.auth.user();
    this.wm.set({ x: 5 + Math.random() * 55, y: 8 + Math.random() * 70, text: `${u?.fullName ?? ''} · ${u?.email ?? ''} · ${new Date().toLocaleString()}` });
  }

  async load(): Promise<void> {
    const seq = ++this.loadSeq;
    this.failed.set(false); this.locked.set(false);
    this.offlineMode.set(false);
    try {
      const saved = this.offline.entry(this.lessonId);
      let lesson: Lesson, course: CourseDetail;
      let pdfShown = false; // becomes true only when this lesson's own PDF bytes really arrived
      if (!this.net.online() && saved) { ({ lesson, course } = saved); if (seq !== this.loadSeq) return; await this.useOffline(saved); pdfShown = true; }
      else {
        try { [lesson, course] = await Promise.all([this.api.lesson(this.lessonId), this.api.course(this.courseId)]); }
        catch (e) {
          if ((e as { status?: number }).status !== 0 || !saved) throw e;
          ({ lesson, course } = saved); await this.useOffline(saved); // the network dropped: fall back to the downloaded copy
          pdfShown = true;
        }
        if (seq !== this.loadSeq) return; // navigated away while loading: drop the stale response
        if (!this.offlineMode() && ['Video', 'Audio', 'Pdf'].includes(lesson.lessonType)) {
          const blob = await this.api.streamBlob(this.lessonId); // authenticated, enrollment-checked fetch of THIS lesson's file; a failure throws
          this.setMedia(blob);
          pdfShown = blob.size > 0; // a blank body is not a viewed PDF
        }
      }
      if (seq !== this.loadSeq) return;
      this.lesson.set(lesson); this.course.set(course); void this.loadCurriculum(course, seq);
      // A PDF counts as viewed only after its own authenticated fetch returned real bytes while still on this
      // lesson (never on navigation intent, a stale response, a failed fetch or a blank body). Offline viewing
      // queues the record and replays it when online.
      if (lesson.lessonType === 'Pdf' && pdfShown) void this.flush(true).then(() => this.refreshProgress());
      if (lesson.lessonType === 'InteractiveSlides') { this.slides.set(await this.api.slides(this.lessonId).catch(() => [])); this.slideIx.set(0); }
      if (lesson.lessonType === 'Video' && seq === this.loadSeq) { this.quizzes = this.offlineMode() ? [] : await this.api.interactiveQuizzes(this.lessonId).catch(() => []); }
      if ((lesson.lessonType === 'Video' || lesson.lessonType === 'Audio') && seq === this.loadSeq && !this.offlineMode()) {
        const tr = await this.api.transcript(this.lessonId).catch(() => null); // offline or old backend: no captions, the lesson still plays
        if (seq !== this.loadSeq) return;
        if (tr?.cues?.length) { this.cues.set(tr.cues); this.ccOn.set(true); }
      }
      if (!this.timer) this.timer = setInterval(() => void this.flush(false), 15000);
    } catch (e) {
      if (seq !== this.loadSeq) return;
      const status = (e as { status?: number }).status;
      if (status === 402 || status === 403) { this.locked.set(true); this.canUnlock.set(true); } else this.failed.set(true);
    }
  }

  private setMedia(blob: Blob): void {
    if (this.url) URL.revokeObjectURL(this.url);
    this.url = URL.createObjectURL(blob); this.mediaUrl.set(this.url); this.safeUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.url));
  }
  private async useOffline(e: OfflineEntry): Promise<void> {
    this.setMedia(await this.offline.open(e.lessonId)); this.offlineMode.set(true);
  }
  async download(): Promise<void> {
    const l = this.lesson(), c = this.course(); if (!l || !c) return;
    this.dlMsg.set('');
    try { await this.offline.download(l, c); this.dlMsg.set('dl.done'); } catch (e) { this.dlMsg.set(`dl.err.${(e as Error).message}`); }
  }
  async removeDownload(): Promise<void> { await this.offline.remove(this.lessonId); this.dlMsg.set('dl.removed'); }
  canDownload(): boolean { return ['Video', 'Audio', 'Pdf'].includes(this.lesson()?.lessonType ?? ''); }

  /** Saves progress: position, seconds watched since the last save, completion flag. */
  private async flush(completed: boolean): Promise<void> {
    const el = this.media()?.nativeElement, l = this.lesson();
    if (!l) return;
    const pos = el ? Math.floor(el.currentTime) : 0;
    const delta = el && !el.paused ? 15 : Math.max(0, pos - this.lastSaved);
    await this.sync.record(l.id, pos, Math.min(delta, 60), completed, this.speed); // saved now, or queued on the device and replayed when online
    this.lastSaved = pos;
  }
  /** Pay-per-lesson: charges the wallet through POST /lessons/{id}/unlock. The server refuses lessons that are not sold on their own. */
  async unlock(): Promise<void> {
    try { await this.api.unlockLesson(this.lessonId); this.unlockMsg.set('player.unlocked'); this.canUnlock.set(false); await this.load(); } catch { this.unlockMsg.set('common.error'); }
  }
  slide(): LessonSlide | null { return this.slides()[this.slideIx()] ?? null; }
  go(d: number): void { const n = this.slideIx() + d; if (n >= 0 && n < this.slides().length) this.slideIx.set(n); }
  /** Media ended: record completion and offer the next lesson. The learner chooses when to move on; nothing navigates by itself. */
  async onEnded(): Promise<void> { await this.flush(true); this.showUpNext.set(true); void this.refreshProgress(); }
  /** Manual "mark complete": records completion and reveals the up-next card; navigation stays with the learner. */
  async complete(): Promise<void> { await this.flush(true); this.showUpNext.set(true); void this.refreshProgress(); }
  private async refreshProgress(): Promise<void> { const cid = this.courseId; try { const p = await this.api.courseProgress(cid); if (cid === this.courseId) this.progress.set(p); } catch { /* progress panel keeps the last known state */ } }
  private async loadCurriculum(c: CourseDetail, seq: number): Promise<void> {
    void this.refreshProgress();
    const entries = await Promise.all(c.sections.flatMap((s) => s.lessons.map(async (l) => {
      try { return [l.id, await this.api.resources(l.id)] as const; } catch { return [l.id, [] as LessonResource[]] as const; } // the API refuses locked lessons; they just show no files
    })));
    if (seq !== this.loadSeq) return; // a newer lesson took over while the map loaded
    this.resMap.set(Object.fromEntries(entries));
  }
  lessonDone(id: string): boolean { return this.progress()?.lessons.find((x) => x.lessonId === id)?.status === 'Completed'; }
  async downloadResource(lessonId: string, r: LessonResource, ev: Event): Promise<void> {
    ev.stopPropagation(); ev.preventDefault();
    try {
      const blob = await this.api.resourceBlob(lessonId, r.id); const a = document.createElement('a');
      a.href = URL.createObjectURL(blob); a.download = r.fileName || r.title; a.click(); URL.revokeObjectURL(a.href);
    } catch { /* the global toast shows the API message */ }
  }
  resSize(b: number): string { return b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`; }
  dur(s: number | null): string { return s ? `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}` : ''; }

  next(): LessonOutline | null {
    const all = (this.course()?.sections ?? []).flatMap((s) => s.lessons);
    const i = all.findIndex((l) => l.id === this.lessonId);
    return i >= 0 && i < all.length - 1 ? all[i + 1] : null;
  }
  /** The cue under the playhead, for the captions overlay and the panel highlight. */
  activeCue(): TranscriptCue | null { const t = this.now(); return this.cues().find((c) => t >= c.start && t < c.end) ?? null; }
  onTime(): void {
    const el = this.media()?.nativeElement; if (!el) return;
    this.now.set(el.currentTime);
    if (this.quiz()) return;
    const due = this.quizzes.find((q) => !this.answered.has(q.id) && el.currentTime >= q.timestampSeconds);
    if (due) { el.pause(); this.quiz.set(due); }
  }
  async answerQuiz(optionId: string): Promise<void> {
    const q = this.quiz(); if (!q) return;
    try { await this.api.answerInteractiveQuiz(q.id, { optionId }); this.answered.add(q.id); this.quiz.set(null); void this.media()?.nativeElement.play(); } catch { this.quizMsg.set('common.error'); }
  }
  setSpeed(v: string): void { this.speed = Number(v); const el = this.media()?.nativeElement; if (el) el.playbackRate = this.speed; }
  seek(seconds: number): void { const el = this.media()?.nativeElement; if (el) { el.currentTime = seconds; void el.play(); } }

  // privacy screen: the picture is covered whenever the window or tab loses focus
  @HostListener('window:blur') onBlur(): void { this.hidden.set(true); this.media()?.nativeElement.pause(); }
  @HostListener('window:focus') onFocus(): void { this.hidden.set(false); }
  @HostListener('document:visibilitychange') onVis(): void { this.hidden.set(document.hidden); if (document.hidden) this.media()?.nativeElement.pause(); }
  @HostListener('contextmenu', ['$event']) noMenu(e: Event): void { if (this.mediaUrl()) e.preventDefault(); }
}
