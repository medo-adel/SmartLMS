import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { LearningService } from '../../core/services/learning.service';
import { AssignmentsService } from '../../core/services/assignments.service';
import { Enrollment } from '../../core/models/learning';
import { AssignmentRow, PeerReviewItem, SubmissionDetail } from '../../core/models/assignments';

/** Student assignments: browse per enrolled course, create or join a team, submit text or an https link, and complete assigned peer reviews. */
@Component({ selector: 'app-homework', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './homework.page.html', styleUrl: './homework.page.scss' })
export class HomeworkPage implements OnInit {
  private readonly learn = inject(LearningService);
  private readonly api = inject(AssignmentsService);

  readonly enrollments = signal<Enrollment[] | null>(null);
  readonly failed = signal(false);
  readonly items = signal<AssignmentRow[]>([]);
  readonly reviews = signal<PeerReviewItem[]>([]);
  readonly msg = signal('');
  readonly openId = signal('');
  readonly mySubmission = signal<Record<string, SubmissionDetail | null>>({});
  readonly teamId = signal<Record<string, string>>({});
  readonly reviewFor = signal('');
  readonly reviewWork = signal<SubmissionDetail | null>(null);
  course = '';
  submitText = ''; submitLink = '';
  teamName = ''; joinCode = '';
  reviewScores: Record<string, number | null> = {};
  reviewComments = '';

  ngOnInit(): void { void this.init(); }
  private async init(): Promise<void> {
    try {
      const [e, r] = await Promise.all([this.learn.myEnrollments(), this.api.myReviews()]);
      this.enrollments.set(e); this.reviews.set(r);
    } catch { this.failed.set(true); }
  }

  async pickCourse(): Promise<void> {
    this.items.set([]); this.openId.set(''); this.msg.set('');
    if (!this.course) return;
    try { this.items.set(await this.api.list(this.course)); } catch { this.msg.set('common.error'); }
  }

  async toggle(a: AssignmentRow): Promise<void> {
    if (this.openId() === a.id) { this.openId.set(''); return; }
    this.openId.set(a.id); this.submitText = ''; this.submitLink = ''; this.teamName = ''; this.joinCode = '';
    if (this.mySubmission()[a.id] !== undefined) return;
    // The list payload carries the caller's newest submission (id + status); open it to show content, grade, feedback and comments.
    if (a.mine?.id) {
      this.mySubmission.update((m) => ({ ...m, [a.id]: null }));
      try { const d = await this.api.submission(a.mine.id); this.mySubmission.update((m) => ({ ...m, [a.id]: d })); } catch { /* older backend or revoked access: the submit form still works */ }
    } else {
      this.mySubmission.update((m) => ({ ...m, [a.id]: null }));
    }
  }

  async createTeam(a: AssignmentRow): Promise<void> {
    if (!this.teamName.trim()) { this.msg.set('hw.errTeam'); return; }
    try {
      const r = await this.api.group(a.id, { groupId: null, name: this.teamName.trim() }) as { groupId?: string };
      if (r && typeof r === 'object' && 'groupId' in r) this.teamId.update((m) => ({ ...m, [a.id]: String((r as { groupId: string }).groupId) }));
      this.msg.set('hw.teamReady');
    } catch { /* toast by interceptor */ }
  }

  async joinTeam(a: AssignmentRow): Promise<void> {
    if (!this.joinCode.trim()) { this.msg.set('hw.errCode'); return; }
    try {
      await this.api.group(a.id, { groupId: this.joinCode.trim(), name: null });
      this.teamId.update((m) => ({ ...m, [a.id]: this.joinCode.trim() }));
      this.msg.set('hw.teamReady');
    } catch { /* toast by interceptor */ }
  }

  /** Text or an https link, per the API contract (https only, text up to 50k chars). */
  async submit(a: AssignmentRow): Promise<void> {
    const text = this.submitText.trim(); const link = this.submitLink.trim();
    if (!text && !link) { this.msg.set('hw.errEmpty'); return; }
    if (link && !link.startsWith('https://')) { this.msg.set('hw.errHttps'); return; }
    try {
      const r = await this.api.submit(a.id, { textContent: text || null, linkUrl: link || null }) as { id?: string; version?: number; isLate?: boolean };
      this.msg.set(r?.isLate ? 'hw.submittedLate' : 'hw.submitted');
      this.submitText = ''; this.submitLink = '';
    } catch { /* toast by interceptor */ }
  }

  /** Peer review: read the assigned work (anonymized by the API) then score each criterion. */
  async openReview(rv: PeerReviewItem): Promise<void> {
    if (this.reviewFor() === rv.id) { this.reviewFor.set(''); return; }
    this.reviewFor.set(rv.id); this.reviewWork.set(null); this.reviewComments = '';
    this.reviewScores = {}; for (const r of rv.rubric) this.reviewScores[r.id] = null;
    try { this.reviewWork.set(await this.api.submission(rv.submissionId)); } catch { this.msg.set('common.error'); }
  }

  async submitReview(rv: PeerReviewItem): Promise<void> {
    const scores = rv.rubric.map((r) => ({ criterionId: r.id, points: this.reviewScores[r.id] ?? 0, feedback: null }));
    try {
      await this.api.review(rv.id, { scores, comments: this.reviewComments.trim() || null });
      this.reviewFor.set(''); this.msg.set('hw.reviewSent');
      this.reviews.set(await this.api.myReviews());
    } catch { /* toast by interceptor */ }
  }
}
