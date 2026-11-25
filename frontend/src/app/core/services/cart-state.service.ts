import { Injectable, inject, signal } from '@angular/core';
import { LearningService } from './learning.service';
import { CartView } from '../models/learning';

/**
 * Shared cart item count for the shell badge. The cart page applies its loaded view,
 * add/remove flows refresh, and the shell loads it once per sign-in so the badge
 * survives a page reload (it comes from the API, not local state).
 */
@Injectable({ providedIn: 'root' })
export class CartState {
  private readonly api = inject(LearningService);
  readonly count = signal(0);
  apply(c: CartView): void { this.count.set(c.items.length); }
  async refresh(): Promise<void> { try { this.apply(await this.api.cart()); } catch { /* keep the last known count */ } }
}
