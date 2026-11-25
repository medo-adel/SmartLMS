import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { CartState } from '../../core/services/cart-state.service';
import { CourseDetail, ExamListItem } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

@Component({ selector: 'app-course-detail', imports: [RouterLink, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './course-detail.page.html', styleUrl: './course-detail.page.scss' })
export class CourseDetailPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly cartState = inject(CartState);
  private readonly route = inject(ActivatedRoute);
  readonly course = signal<CourseDetail | null>(null);
  readonly exams = signal<ExamListItem[]>([]);
  readonly enrolled = signal(false);
  readonly failed = signal(false);
  readonly message = signal('');

  ngOnInit(): void { void this.load(); }
  private get id(): string { return this.route.snapshot.paramMap.get('id') ?? ''; }
  async load(): Promise<void> {
    this.failed.set(false);
    try {
      this.course.set(await this.api.course(this.id));
      const mine = await this.api.myEnrollments().catch(() => []);
      this.enrolled.set(mine.some((e) => e.courseId === this.id && e.status === 'Active' || e.status === 'Completed'));
      if (this.enrolled()) this.exams.set(await this.api.exams(this.id).catch(() => []));
    } catch { this.failed.set(true); }
  }
  get isFree(): boolean { const c = this.course(); return !c || c.pricingModel === 'Free' || !c.price; }
  async enroll(): Promise<void> {
    try { await this.api.enroll(this.id); this.message.set('course.enrolled'); await this.load(); } catch { /* error toast comes from the interceptor */ }
  }
  async addToCart(): Promise<void> {
    try { await this.api.addToCart('Course', this.id); this.message.set('nav.cart'); void this.cartState.refresh(); } catch { /* toast */ }
  }
  minutes(s: number | null): string { return s ? `${Math.max(1, Math.round(s / 60))} min` : ''; }
}
