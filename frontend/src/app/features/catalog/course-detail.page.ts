import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { CommunityService } from '../../core/services/community.service';
import { ReviewsView } from '../../core/models/community';
import { CartState } from '../../core/services/cart-state.service';
import { CourseDetail, CourseProgress, ExamListItem } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

@Component({ selector: 'app-course-detail', imports: [RouterLink, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './course-detail.page.html', styleUrl: './course-detail.page.scss' })
export class CourseDetailPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly cartState = inject(CartState);
  private readonly route = inject(ActivatedRoute);
  readonly course = signal<CourseDetail | null>(null);
  readonly exams = signal<ExamListItem[]>([]);
  readonly enrolled = signal(false);
  /** Per-lesson + overall progress for enrolled students, straight from GET /courses/{id}/progress (persisted server-side). */
  readonly progress = signal<CourseProgress | null>(null);
  readonly failed = signal(false);
  readonly message = signal('');
  /** True when the course is in the student's wishlist (server is the source of truth). */
  readonly wishlisted = signal(false);
  private readonly community = inject(CommunityService);
  /** Published reviews + aggregate; myReview is the caller's own row (any status). */
  readonly reviews = signal<ReviewsView | null>(null);
  reviewRating = 5;
  reviewBody = '';
  readonly reviewBusy = signal(false);

  ngOnInit(): void { void this.load(); }
  private get id(): string { return this.route.snapshot.paramMap.get('id') ?? ''; }
  async load(): Promise<void> {
    this.failed.set(false);
    try {
      this.course.set(await this.api.course(this.id));
      const mine = await this.api.myEnrollments().catch(() => []);
      this.enrolled.set(mine.some((e) => e.courseId === this.id && e.status === 'Active' || e.status === 'Completed'));
      if (this.enrolled()) {
        this.exams.set(await this.api.exams(this.id).catch(() => []));
        this.progress.set(await this.api.courseProgress(this.id).catch(() => null)); // 404/403 -> no bar, never invented numbers
      }
      const wl = await this.api.wishlist().catch(() => null); // 403 for non-students -> hide the heart state
      this.wishlisted.set(!!wl?.items && wl.items.some((i) => i.courseId === this.id));
      const rv = await this.community.reviews(this.id).catch(() => null);
      this.reviews.set(rv);
      if (rv?.myReview) { this.reviewRating = rv.myReview.rating; this.reviewBody = rv.myReview.body ?? ''; }
    } catch { this.failed.set(true); }
  }
  get isFree(): boolean { const c = this.course(); return !c || c.pricingModel === 'Free' || !c.price; }
  async enroll(): Promise<void> {
    try { await this.api.enroll(this.id); this.message.set('course.enrolled'); await this.load(); } catch { /* error toast comes from the interceptor */ }
  }
  async addToCart(): Promise<void> {
    try { await this.api.addToCart('Course', this.id); this.message.set('nav.cart'); void this.cartState.refresh(); } catch { /* toast */ }
  }
  async toggleWishlist(): Promise<void> {
    try {
      if (this.wishlisted()) { await this.api.removeFromWishlist(this.id); this.wishlisted.set(false); this.message.set('wishlist.removed'); }
      else { await this.api.addToWishlist(this.id); this.wishlisted.set(true); this.message.set('wishlist.added'); }
    } catch { /* interceptor toast */ }
  }
  stars(n: number): string { return '★★★★★'.slice(0, Math.round(n)) + '☆☆☆☆☆'.slice(0, 5 - Math.round(n)); }
  async submitReview(): Promise<void> {
    this.reviewBusy.set(true);
    try { await this.community.upsertReview(this.id, this.reviewRating, this.reviewBody.trim() || null); this.reviews.set(await this.community.reviews(this.id)); this.message.set('reviews.thanks'); }
    catch { /* interceptor toast */ } finally { this.reviewBusy.set(false); }
  }
  async deleteReview(): Promise<void> {
    this.reviewBusy.set(true);
    try { await this.community.deleteReview(this.id); this.reviewRating = 5; this.reviewBody = ''; this.reviews.set(await this.community.reviews(this.id)); }
    catch { /* interceptor toast */ } finally { this.reviewBusy.set(false); }
  }
  readonly reportOpen = signal('');
  reportReason = '';
  async reportReview(reviewId: string): Promise<void> {
    if (!this.reportReason.trim()) return;
    try { await this.community.reportReview(reviewId, this.reportReason.trim()); this.reportOpen.set(''); this.reportReason = ''; this.message.set('reviews.reported'); } catch { /* interceptor toast */ }
  }
  minutes(s: number | null): string { return s ? `${Math.max(1, Math.round(s / 60))} min` : ''; }
  /** Lesson completion state and how far through the media the student is (0-1), from the persisted position. */
  lessonState(lessonId: string): { done: boolean; fraction: number; started: boolean } | null {
    const pr = this.progress(); if (!pr) return null;
    const row = pr.lessons.find((x) => x.lessonId === lessonId);
    if (!row || row.status === 'NotStarted') return null;
    if (row.status === 'Completed') return { done: true, fraction: 1, started: true };
    const dur = this.course()?.sections.flatMap((s) => s.lessons).find((x) => x.id === lessonId)?.durationSeconds ?? 0;
    const fraction = dur > 0 ? Math.min(1, Math.max(0, row.lastPositionSeconds / dur)) : 0; // no duration -> a plain started dot, never a guessed slice
    return { done: false, fraction, started: true };
  }
  /** Pie fill for the fractional ring: the real fraction, so a quarter read is a quarter watched. */
  ringStyle(fraction: number): string { return `conic-gradient(var(--ion-color-primary, #0f766e) ${Math.round(fraction * 100)}%, var(--ion-color-step-150, #e5e7eb) 0)`; }
}
