import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { AuthService } from '../../core/services/auth.service';
import { AdminService } from '../../core/services/admin.service';
import { LearningService } from '../../core/services/learning.service';
import { FinanceService, AdminDashboard, Debits, Held, HeldItem, MyRevenue, Payouts, Shares, Statement } from '../../core/services/finance.service';

type Tab = 'overview' | 'shares' | 'statement' | 'held' | 'debits' | 'payouts' | 'mine' | 'controls';

/** Finance: admin dashboard, instructor shares, payout statement, held revenue, debits, payouts, refunds (TenantAdmin). Instructors see only their own revenue. */
@Component({ selector: 'app-finance', imports: [DatePipe, DecimalPipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './finance.page.html', styleUrl: './finance.page.scss' })
export class FinancePage implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(FinanceService);
  private readonly admin = inject(AdminService);
  private readonly learning = inject(LearningService);
  readonly tab = signal<Tab>('overview');
  readonly dash = signal<AdminDashboard | null>(null);
  readonly shares = signal<Shares | null>(null);
  readonly statement = signal<Statement | null>(null);
  readonly held = signal<Held | null>(null);
  readonly debits = signal<Debits | null>(null);
  readonly payouts = signal<Payouts | null>(null);
  readonly mine = signal<MyRevenue | null>(null);
  readonly msg = signal('');
  // resolved display names; fall back to the full identifier when a lookup fails
  readonly courseNames = signal<Record<string, string>>({});
  readonly userNames = signal<Record<string, string>>({});
  // pending confirmations carry the full context of the write before it is sent
  readonly pendingRelease = signal<{ item: HeldItem; percent: number } | null>(null);
  readonly pendingRefund = signal<{ orderId: string; amount: number; reference: string; reason: string | null } | null>(null);
  readonly pendingPayout = signal<{ id: string; name: string; period: string; amount: number; currency: string; status: string; reference: string | null } | null>(null);
  defaultShare = '';
  stFrom = ''; stTo = '';
  heldFrom = ''; heldTo = '';
  calcFrom = ''; calcTo = '';
  refundOrder = ''; refundAmount = ''; refundRef = ''; refundReason = '';
  releasePct: Record<string, number | null> = {};
  payoutEdit: string | null = null; payoutStatus = 'Approved'; payoutRef = '';
  readonly payoutStatuses = ['Calculated', 'Approved', 'Paid', 'Cancelled'];

  ngOnInit(): void {
    if (this.auth.hasRole('TenantAdmin')) { void this.loadDash(); void this.loadShares(); void this.loadDebits(); void this.loadPayouts(); void this.loadControlsData(); }
    if (this.auth.hasRole('Instructor') && !this.auth.hasRole('TenantAdmin')) { this.tab.set('mine'); void this.loadMine(); }
  }

  go(t: Tab): void { this.msg.set(''); this.pendingRelease.set(null); this.pendingRefund.set(null); this.pendingPayout.set(null); this.pendingOverride.set(null); this.pendingControls.set(null); this.pendingWeight.set(null); this.tab.set(t); }

  courseLabel(id: string): string { return this.courseNames()[id] ?? id; }
  userLabel(id: string): string { return this.userNames()[id] ?? id; }

  private resolveNames(courseIds: string[], userIds: string[]): void {
    for (const id of new Set(courseIds)) {
      if (this.courseNames()[id]) continue;
      this.learning.course(id).then((c) => this.courseNames.update((m) => ({ ...m, [id]: c.title }))).catch(() => undefined);
    }
    for (const id of new Set(userIds)) {
      if (this.userNames()[id]) continue;
      this.admin.user(id).then((u) => this.userNames.update((m) => ({ ...m, [id]: u.fullName }))).catch(() => undefined);
    }
  }

  async loadDash(): Promise<void> { try { this.dash.set(await this.api.dashboard()); } catch { /* toast */ } }
  async loadShares(): Promise<void> {
    try {
      const s = await this.api.shares(); this.shares.set(s);
      this.defaultShare = s.defaultInstructorSharePercent?.toString() ?? '';
      this.resolveNames(s.overrides.map((o) => o.courseId), s.overrides.map((o) => o.instructorId));
    } catch { /* toast */ }
  }
  async loadMine(): Promise<void> { try { this.mine.set(await this.api.myRevenue()); } catch { /* toast */ } }
  async loadDebits(): Promise<void> {
    try { const d = await this.api.debits(); this.debits.set(d); this.resolveNames([], d.items.map((i) => i.instructorId)); } catch { /* toast */ }
  }
  async loadPayouts(): Promise<void> { try { this.payouts.set(await this.api.payouts()); } catch { /* toast */ } }

  async saveDefaultShare(): Promise<void> {
    const raw: unknown = this.defaultShare;
    const v = raw === '' || raw === null || raw === undefined ? null : Number(raw);
    if (v !== null && (Number.isNaN(v) || v < 0 || v > 100)) { this.msg.set('fin.errPct'); return; }
    try { await this.api.setDefaultShare(v); this.msg.set('fin.saved'); await this.loadShares(); } catch { /* toast */ }
  }

  async loadStatement(): Promise<void> {
    if (!this.stFrom || !this.stTo) { this.msg.set('fin.errPeriod'); return; }
    try {
      const st = await this.api.statement(new Date(this.stFrom).toISOString(), new Date(this.stTo).toISOString());
      this.statement.set(st); this.resolveNames([], st.instructors.map((i) => i.instructorId));
    } catch { /* toast */ }
  }

  async loadHeld(): Promise<void> {
    if (!this.heldFrom || !this.heldTo) { this.msg.set('fin.errPeriod'); return; }
    try {
      const h = await this.api.held(new Date(this.heldFrom).toISOString(), new Date(this.heldTo).toISOString());
      this.held.set(h); this.resolveNames(h.items.map((i) => i.courseId), h.items.map((i) => i.instructorId));
    } catch { /* toast */ }
  }

  reviewRelease(h: HeldItem): void {
    const pct = this.releasePct[h.orderItemId];
    if (pct === null || pct === undefined || Number.isNaN(pct) || pct < 0 || pct > 100) { this.msg.set('fin.errPct'); return; }
    this.pendingRelease.set({ item: h, percent: pct });
  }

  async confirmRelease(): Promise<void> {
    const p = this.pendingRelease(); if (!p) return;
    try {
      await this.api.releaseHeld({ orderItemId: p.item.orderItemId, courseId: p.item.courseId, instructorId: p.item.instructorId, percent: p.percent });
      this.pendingRelease.set(null); this.msg.set('fin.released');
      if (this.heldFrom && this.heldTo) await this.loadHeld();
    } catch { /* toast */ }
  }

  readonly calcJob = signal<{ status: string; progressPercent: number; errorMessage: string | null } | null>(null);

  async calculate(): Promise<void> {
    if (!this.calcFrom || !this.calcTo) { this.msg.set('fin.errPeriod'); return; }
    try {
      const r = await this.api.calculatePayouts(new Date(this.calcFrom).toISOString(), new Date(this.calcTo).toISOString());
      this.msg.set('fin.calcDone');
      if (r.jobId) { this.calcJob.set({ status: 'Queued', progressPercent: 0, errorMessage: null }); void this.pollJob(r.jobId); } else { await this.loadPayouts(); }
    } catch { /* toast */ }
  }

  private async pollJob(jobId: string): Promise<void> {
    // GET /jobs/{id}: any authenticated caller can read their own job; poll until it settles, then refresh the payouts list.
    for (let i = 0; i < 60; i++) {
      await new Promise((r2) => setTimeout(r2, 5000));
      try {
        const j = await this.api.job(jobId);
        this.calcJob.set({ status: j.status, progressPercent: j.progressPercent, errorMessage: j.errorMessage });
        if (j.status === 'Succeeded') { this.calcJob.set(null); this.msg.set('fin.calcFinished'); await this.loadPayouts(); return; }
        if (j.status === 'Failed') { this.msg.set('fin.calcFailed'); return; }
      } catch { /* transient poll failure: keep polling */ }
    }
  }

  startPayoutEdit(id: string, current: string, ref: string | null): void { this.payoutEdit = id; this.payoutStatus = current; this.payoutRef = ref ?? ''; }

  reviewPayout(po: { id: string; name: string | null; instructorId: string; periodStartUtc: string; periodEndUtc: string; amount: number; currency: string }): void {
    this.pendingPayout.set({ id: po.id, name: po.name ?? this.userLabel(po.instructorId), period: `${po.periodStartUtc.slice(0, 10)} – ${po.periodEndUtc.slice(0, 10)}`, amount: po.amount, currency: po.currency, status: this.payoutStatus, reference: this.payoutRef.trim() || null });
  }

  async confirmPayout(): Promise<void> {
    const p = this.pendingPayout(); if (!p) return;
    try { await this.api.updatePayout(p.id, p.status, p.reference); this.pendingPayout.set(null); this.payoutEdit = null; this.msg.set('fin.saved'); await this.loadPayouts(); } catch { /* toast */ }
  }

  // ---- per-course share overrides, subscription controls, learning weight (TenantAdmin) ---------------------------
  readonly courses = signal<{ id: string; title: string }[] | null>(null);
  readonly instructors = signal<{ id: string; fullName: string }[]>([]);
  ov: { course: string; instructor: string; percent: string | number | null } = { course: '', instructor: '', percent: '' };
  readonly pendingOverride = signal<{ courseId: string; instructorId: string; percent: number; courseName: string; instructorName: string } | null>(null);
  controls: { minWatch: string | number | null; minAvg: string | number | null; jump: string | number | null } = { minWatch: '', minAvg: '', jump: '' };
  readonly controlsLoaded = signal(false);
  readonly pendingControls = signal<{ minWatchPercent: number | null; minAverageLessonSeconds: number | null; jumpAlertPercent: number | null } | null>(null);
  lw: { course: string; minutes: string | number | null } = { course: '', minutes: '' };
  readonly lwCurrent = signal<number | null>(null);
  readonly lwUnavailable = signal(false);
  readonly pendingWeight = signal<{ courseId: string; seconds: number; courseName: string } | null>(null);

  private async loadControlsData(): Promise<void> {
    try { this.courses.set((await this.learning.myCourses()).map((c) => ({ id: c.id, title: c.title }))); } catch { /* toast */ }
    try { this.instructors.set((await this.admin.users('', 'Instructor')).items.map((u) => ({ id: u.id, fullName: u.fullName }))); } catch { /* toast */ }
    try {
      const c = await this.api.subscriptionControls();
      this.controls = { minWatch: c.minWatchPercent?.toString() ?? '', minAvg: c.minAverageLessonSeconds?.toString() ?? '', jump: c.jumpAlertPercent?.toString() ?? '' };
      this.controlsLoaded.set(true);
    } catch { /* toast */ }
  }

  reviewOverride(): void {
    const pct = Number(this.ov.percent);
    if (!this.ov.course || !this.ov.instructor || this.ov.percent === '' || this.ov.percent === null || Number.isNaN(pct) || pct < 0 || pct > 100) { this.msg.set('fin.errPct'); return; }
    const courseName = (this.courses() ?? []).find((c) => c.id === this.ov.course)?.title ?? this.ov.course;
    const instructorName = this.instructors().find((i) => i.id === this.ov.instructor)?.fullName ?? this.ov.instructor;
    this.pendingOverride.set({ courseId: this.ov.course, instructorId: this.ov.instructor, percent: pct, courseName, instructorName });
  }
  async confirmOverride(): Promise<void> {
    const p = this.pendingOverride(); if (!p) return;
    try {
      await this.api.setCourseShare(p.courseId, p.instructorId, p.percent);
      this.pendingOverride.set(null); this.ov = { course: '', instructor: '', percent: '' };
      this.msg.set('fin.saved'); await this.loadShares();
    } catch { /* toast */ }
  }
  reviewControls(): void {
    const num = (v: string | number | null) => (v === null || v === '') ? null : Number(v);
    const mw = num(this.controls.minWatch), ma = num(this.controls.minAvg), jp = num(this.controls.jump);
    if ((mw !== null && (Number.isNaN(mw) || mw < 0 || mw > 100)) || (ma !== null && (Number.isNaN(ma) || ma < 0)) || (jp !== null && (Number.isNaN(jp) || jp < 0))) { this.msg.set('fin.errControls'); return; }
    this.pendingControls.set({ minWatchPercent: mw, minAverageLessonSeconds: ma, jumpAlertPercent: jp });
  }
  async confirmControls(): Promise<void> {
    const p = this.pendingControls(); if (!p) return;
    try {
      const c = await this.api.setSubscriptionControls(p);
      this.controls = { minWatch: c.minWatchPercent?.toString() ?? '', minAvg: c.minAverageLessonSeconds?.toString() ?? '', jump: c.jumpAlertPercent?.toString() ?? '' };
      this.pendingControls.set(null); this.msg.set('fin.saved');
    } catch { /* toast */ }
  }
  async pickWeightCourse(): Promise<void> {
    this.lwCurrent.set(null); this.lwUnavailable.set(false); this.lw.minutes = '';
    if (!this.lw.course) return;
    try {
      const w = await this.api.learningWeight(this.lw.course);
      this.lwCurrent.set(w.nonVideoCourseSeconds); this.lw.minutes = w.nonVideoCourseSeconds !== null ? (w.nonVideoCourseSeconds / 60).toString() : '';
    } catch { this.lwUnavailable.set(true); }
  }
  reviewWeight(): void {
    const mins = Number(this.lw.minutes);
    if (!this.lw.course || this.lw.minutes === '' || this.lw.minutes === null || Number.isNaN(mins) || mins < 0 || mins > 6000) { this.msg.set('fin.errWeight'); return; }
    const courseName = (this.courses() ?? []).find((c) => c.id === this.lw.course)?.title ?? this.lw.course;
    this.pendingWeight.set({ courseId: this.lw.course, seconds: Math.round(mins * 60), courseName });
  }
  async confirmWeight(): Promise<void> {
    const p = this.pendingWeight(); if (!p) return;
    try {
      await this.api.setLearningWeight(p.courseId, p.seconds);
      this.pendingWeight.set(null); this.msg.set('fin.saved');
      this.lwCurrent.set(p.seconds);
    } catch { /* toast */ }
  }

  reviewRefund(): void {
    const amount = Number(this.refundAmount);
    if (!this.refundOrder.trim() || Number.isNaN(amount) || amount <= 0 || !this.refundRef.trim()) { this.msg.set('fin.errRefund'); return; }
    this.pendingRefund.set({ orderId: this.refundOrder.trim(), amount, reference: this.refundRef.trim(), reason: this.refundReason.trim() || null });
  }

  async confirmRefund(): Promise<void> {
    const p = this.pendingRefund(); if (!p) return;
    try {
      await this.api.recordRefund(p.orderId, p.amount, p.reference, p.reason);
      this.pendingRefund.set(null); this.refundOrder = ''; this.refundAmount = ''; this.refundRef = ''; this.refundReason = '';
      this.msg.set('fin.refundDone'); await this.loadDebits();
    } catch { /* toast */ }
  }
}
