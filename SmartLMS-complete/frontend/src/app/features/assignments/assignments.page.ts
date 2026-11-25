import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { AuthService } from '../../core/services/auth.service';
import { LearningService } from '../../core/services/learning.service';
import { AssignmentsService } from '../../core/services/assignments.service';
import { MyCourse } from '../../core/models/learning';
import { AssignmentRequest, AssignmentRow, RubricItem, SubmissionDetail, SubmissionRow } from '../../core/models/assignments';

const EMPTY_FORM: AssignmentRequest = {
  title: '', instructions: '', dueAtUtc: null, maxPoints: 100,
  allowResubmission: false, maxResubmissions: null, isGroup: false, maxGroupSize: null,
  peerReviewEnabled: false, peerReviewsPerSubmission: null, isPublished: false, rubric: [],
};

/** Instructor/TA assignment management and grading desk. The API enforces: manage = TenantAdmin/Instructor, grade = +TeachingAssistant. */
@Component({ selector: 'app-assignments', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './assignments.page.html', styleUrl: './assignments.page.scss' })
export class AssignmentsPage implements OnInit {
  private readonly learn = inject(LearningService);
  private readonly api = inject(AssignmentsService);
  readonly auth = inject(AuthService);

  readonly courses = signal<MyCourse[] | null>(null);
  readonly failed = signal(false);
  readonly items = signal<AssignmentRow[]>([]);
  readonly msg = signal('');
  readonly editing = signal<string>(''); // '' closed, 'new' create, otherwise assignment id
  readonly subsFor = signal<string>('');
  readonly subs = signal<SubmissionRow[]>([]);
  readonly confirmReturn = signal('');
  readonly gradingFor = signal<string>('');
  readonly detail = signal<SubmissionDetail | null>(null);
  readonly detailFailed = signal(false);
  course = '';
  dueLocal = '';
  form: AssignmentRequest = { ...EMPTY_FORM };
  grade: { totalScore: number | null; feedback: string; scores: Record<string, number | null> } = { totalScore: null, feedback: '', scores: {} };
  commentText = '';

  readonly canManage = computed(() => this.auth.hasRole('TenantAdmin', 'Instructor'));
  readonly canGrade = computed(() => this.auth.hasRole('TenantAdmin', 'Instructor', 'TeachingAssistant'));
  readonly activeAssignment = computed(() => this.items().find((a) => a.id === this.subsFor()) ?? null);

  ngOnInit(): void { void this.init(); }
  private async init(): Promise<void> { try { this.courses.set(await this.learn.myCourses()); } catch { this.failed.set(true); } }

  async pickCourse(): Promise<void> {
    this.items.set([]); this.subsFor.set(''); this.editing.set(''); this.msg.set('');
    if (!this.course) return;
    try { this.items.set(await this.api.list(this.course)); } catch { this.msg.set('common.error'); }
  }

  startCreate(): void { this.form = { ...EMPTY_FORM, rubric: [] }; this.dueLocal = ''; this.editing.set('new'); }
  startEdit(a: AssignmentRow): void {
    this.form = {
      title: a.title, instructions: a.instructions ?? '', dueAtUtc: a.dueAtUtc, maxPoints: a.maxPoints,
      allowResubmission: a.allowResubmission, maxResubmissions: null, isGroup: a.isGroup, maxGroupSize: a.maxGroupSize,
      peerReviewEnabled: a.peerReviewEnabled, peerReviewsPerSubmission: null, isPublished: a.isPublished,
      rubric: a.rubric.map((r) => ({ name: r.name, description: r.description, maxPoints: r.maxPoints })),
    };
    this.dueLocal = a.dueAtUtc ? a.dueAtUtc.slice(0, 16) : '';
    this.editing.set(a.id);
  }
  addCriterion(): void { (this.form.rubric ??= []).push({ name: '', description: null, maxPoints: 10 }); }
  removeCriterion(i: number): void { this.form.rubric?.splice(i, 1); }

  async save(): Promise<void> {
    if (!this.form.title.trim()) { this.msg.set('asg.errTitle'); return; }
    const rubric = (this.form.rubric ?? []).filter((r) => r.name.trim());
    const body: AssignmentRequest = {
      ...this.form,
      instructions: this.form.instructions?.trim() || null,
      dueAtUtc: this.dueLocal ? new Date(this.dueLocal).toISOString() : null,
      rubric: rubric.length ? rubric : null,
    };
    try {
      if (this.editing() === 'new') await this.api.create(this.course, body);
      else await this.api.update(this.editing(), body);
      this.editing.set(''); this.msg.set('asg.saved'); await this.pickCourse();
    } catch { /* toast by interceptor */ }
  }

  async toggleSubs(a: AssignmentRow): Promise<void> {
    if (this.subsFor() === a.id) { this.subsFor.set(''); return; }
    this.subsFor.set(a.id); this.subs.set([]); this.gradingFor.set('');
    try { this.subs.set(await this.api.submissions(a.id)); } catch { this.msg.set('common.error'); }
  }

  /** Opens the grading desk: loads the submission's actual content, existing feedback, comments and prior scores first. */
  async startGrade(s: SubmissionRow): Promise<void> {
    if (this.gradingFor() === s.id) { this.gradingFor.set(''); return; }
    const a = this.activeAssignment();
    this.grade = { totalScore: s.totalScore, feedback: '', scores: {} };
    for (const r of a?.rubric ?? []) this.grade.scores[r.id] = null;
    this.commentText = '';
    this.detail.set(null); this.detailFailed.set(false);
    this.gradingFor.set(s.id);
    try {
      const d = await this.api.submission(s.id);
      this.detail.set(d);
      this.grade.totalScore = d.totalScore;
      this.grade.feedback = d.feedback ?? '';
      for (const sc of d.scores) if (sc.criterionId in this.grade.scores) this.grade.scores[sc.criterionId] = sc.points;
    } catch { this.detailFailed.set(true); }
  }

  async saveGrade(s: SubmissionRow): Promise<void> {
    const a = this.activeAssignment();
    const scores = (a?.rubric ?? []).map((r) => ({ criterionId: r.id, points: this.grade.scores[r.id] ?? 0, feedback: null }));
    try {
      await this.api.grade(s.id, { scores: scores.length ? scores : null, totalScore: scores.length ? null : this.grade.totalScore, feedback: this.grade.feedback.trim() || null });
      if (this.commentText.trim()) await this.api.comment(s.id, this.commentText.trim());
      this.gradingFor.set(''); this.msg.set('asg.graded');
      this.subs.set(await this.api.submissions(this.subsFor()));
    } catch { /* toast by interceptor */ }
  }

  /** Two-tap confirmation: returning sends the submission back to the student for resubmission. */
  async returnSub(s: SubmissionRow): Promise<void> {
    if (this.confirmReturn() !== s.id) { this.confirmReturn.set(s.id); return; }
    this.confirmReturn.set('');
    try {
      await this.api.returnForResubmission(s.id, { feedback: this.grade.feedback.trim() || null });
      this.gradingFor.set(''); this.msg.set('asg.returned');
      this.subs.set(await this.api.submissions(this.subsFor()));
    } catch { /* toast by interceptor */ }
  }

  async assignPeer(a: AssignmentRow): Promise<void> {
    try { await this.api.assignPeerReviews(a.id); this.msg.set('asg.peerAssigned'); } catch { /* toast by interceptor */ }
  }
}
