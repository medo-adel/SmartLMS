import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { Bundle, CourseListItem, CouponDto, Plan } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Tenant admin: coupons, bundles, subscription plans and affiliate profiles. Each form posts to the real route; the API validates. */
@Component({ selector: 'app-monetization', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './monetization.page.html', styleUrl: './monetization.page.scss' })
export class MonetizationPage implements OnInit {
  private readonly api = inject(LearningService);
  readonly coupons = signal<CouponDto[] | null>(null);
  readonly bundles = signal<Bundle[]>([]);
  readonly plans = signal<Plan[]>([]);
  readonly courses = signal<CourseListItem[]>([]);
  readonly failed = signal(false);
  readonly info = signal('');
  cp = { code: '', type: 'Percentage', value: 10, max: null as number | null, perUser: 1 as number | null, min: null as number | null, expires: '', courseId: '' };
  bd = { name: '', description: '', price: 0, currency: 'EGP', ids: [] as string[] };
  pl = { name: '', description: '', price: 0, currency: 'EGP', period: 'Monthly', scope: 'AllCourses', ids: [] as string[] };
  af = { userId: '', percent: 10, code: '' };

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const [c, b, p, k] = await Promise.all([this.api.coupons(), this.api.bundles(), this.api.plans(), this.api.courses('')]);
      this.coupons.set(c); this.bundles.set(b); this.plans.set(p); this.courses.set(k.items);
    } catch { this.failed.set(true); }
  }
  toggleId(list: string[], id: string): string[] { return list.includes(id) ? list.filter((x) => x !== id) : [...list, id]; }
  async createCoupon(): Promise<void> {
    if (!this.cp.code.trim() || !(this.cp.value > 0)) return;
    try {
      await this.api.createCoupon({ code: this.cp.code.trim(), discountType: this.cp.type, value: Number(this.cp.value), maxRedemptions: this.cp.max ? Number(this.cp.max) : null, maxPerUser: this.cp.perUser ? Number(this.cp.perUser) : null, minOrderAmount: this.cp.min ? Number(this.cp.min) : null, expiresAtUtc: this.cp.expires ? new Date(this.cp.expires + 'T23:59:59Z').toISOString() : null, courseId: this.cp.courseId || null, isActive: true });
      this.cp.code = ''; this.info.set('coupon'); await this.load();
    } catch { /* toast */ }
  }
  async toggleCoupon(c: CouponDto): Promise<void> {
    try { await this.api.updateCoupon(c.id, { code: c.code, discountType: c.discountType, value: c.value, maxRedemptions: c.maxRedemptions, maxPerUser: c.maxPerUser, minOrderAmount: c.minOrderAmount, expiresAtUtc: c.expiresAtUtc, courseId: c.courseId, isActive: !c.isActive }); await this.load(); } catch { /* toast */ }
  }
  async createBundle(): Promise<void> {
    if (!this.bd.name.trim() || this.bd.ids.length < 2 || !(this.bd.price > 0)) return;
    try { await this.api.createBundle({ name: this.bd.name.trim(), description: this.bd.description.trim() || null, price: Number(this.bd.price), currency: this.bd.currency || null, courseIds: this.bd.ids }); this.bd.name = ''; this.bd.ids = []; this.info.set('bundle'); await this.load(); } catch { /* toast */ }
  }
  async createPlan(): Promise<void> {
    if (!this.pl.name.trim() || !(this.pl.price > 0) || (this.pl.scope === 'SelectedCourses' && !this.pl.ids.length)) return;
    try { await this.api.createPlan({ name: this.pl.name.trim(), description: this.pl.description.trim() || null, price: Number(this.pl.price), currency: this.pl.currency || null, billingPeriod: this.pl.period, scope: this.pl.scope, courseIds: this.pl.scope === 'SelectedCourses' ? this.pl.ids : null }); this.pl.name = ''; this.pl.ids = []; this.info.set('plan'); await this.load(); } catch { /* toast */ }
  }
  async createAffiliate(): Promise<void> {
    if (!/^[0-9a-fA-F-]{36}$/.test(this.af.userId.trim())) return;
    try { await this.api.createAffiliate(this.af.userId.trim(), Number(this.af.percent), this.af.code.trim() || null); this.af.userId = ''; this.af.code = ''; this.info.set('affiliate'); } catch { /* toast */ }
  }
}
