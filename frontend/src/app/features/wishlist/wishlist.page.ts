import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonContent, IonButton } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { CartState } from '../../core/services/cart-state.service';
import { WishlistItem } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Saved-for-later courses. Rows come only from GET /wishlist; a vanished course is dropped server-side, never invented here. */
@Component({ selector: 'app-wishlist', imports: [RouterLink, IonContent, IonButton, TranslatePipe], templateUrl: './wishlist.page.html', styleUrl: './wishlist.page.scss' })
export class WishlistPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly cartState = inject(CartState);
  readonly items = signal<WishlistItem[] | null>(null);
  readonly failed = signal(false);
  readonly message = signal('');

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { this.items.set((await this.api.wishlist()).items); } catch { this.failed.set(true); }
  }
  async remove(courseId: string): Promise<void> {
    try { await this.api.removeFromWishlist(courseId); this.message.set('wishlist.removed'); await this.load(); } catch { /* interceptor toast */ }
  }
  async moveToCart(i: WishlistItem): Promise<void> {
    try { await this.api.addToCart('Course', i.courseId); await this.api.removeFromWishlist(i.courseId); this.message.set('nav.cart'); void this.cartState.refresh(); await this.load(); } catch { /* interceptor toast */ }
  }
}
