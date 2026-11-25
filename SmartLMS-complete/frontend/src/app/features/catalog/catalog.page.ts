import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonContent, IonButton } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { AuthService } from '../../core/services/auth.service';
import { Bundle, CourseListItem, Plan } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

@Component({ selector: 'app-catalog', imports: [FormsModule, RouterLink, IonContent, IonButton, TranslatePipe], templateUrl: './catalog.page.html', styleUrl: './catalog.page.scss' })
export class CatalogPage implements OnInit {
  private readonly api = inject(LearningService);
  search = '';
  readonly items = signal<CourseListItem[] | null>(null);
  readonly failed = signal(false);
  readonly bundles = signal<Bundle[]>([]);
  readonly plans = signal<Plan[]>([]);
  readonly added = signal('');
  readonly auth = inject(AuthService);
  ngOnInit(): void { void this.load(); void this.loadOffers(); }
  /** Bundles and subscription plans are optional extras; a failure here never hides the course list. */
  async loadOffers(): Promise<void> {
    const [b, p] = await Promise.allSettled([this.api.bundles(), this.api.plans()]);
    this.bundles.set(b.status === 'fulfilled' ? b.value : []); this.plans.set(p.status === 'fulfilled' ? p.value : []);
  }
  async addOffer(type: 'Bundle' | 'Plan', id: string): Promise<void> { try { await this.api.addToCart(type, id); this.added.set(id); } catch { /* toast */ } }
  async load(): Promise<void> {
    this.failed.set(false);
    try { this.items.set((await this.api.courses(this.search)).items); } catch { this.failed.set(true); }
  }
}
