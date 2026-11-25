import { cardOutline, pricetagsOutline, downloadOutline, cloudOfflineOutline, receiptOutline, helpCircleOutline, businessOutline, peopleOutline, checkmarkDoneOutline, settingsOutline } from 'ionicons/icons';
import { Component, computed, effect, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { IonSplitPane, IonMenu, IonContent, IonList, IonItem, IonIcon, IonLabel, IonTabs, IonTabBar, IonTabButton, IonButton, MenuController } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { chatbubblesOutline, clipboardOutline, createOutline, ellipsisHorizontalOutline, homeOutline, notificationsOutline, personOutline, logOutOutline, bookOutline, gitBranchOutline, sparklesOutline, walletOutline, cartOutline, constructOutline, trophyOutline, cashOutline, shieldCheckmarkOutline } from 'ionicons/icons';
import { AuthService } from '../../core/services/auth.service';
import { OfflineService } from '../../core/services/offline.service';
import { NetworkService } from '../../core/services/network.service';
import { ProgressSyncService } from '../../core/services/progress-sync.service';
import { TenantService } from '../../core/services/tenant.service';
import { CartState } from '../../core/services/cart-state.service';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/**
 * Adaptive shell. Desktop (>= 992px): ion-split-pane with a permanent sidebar (ion-menu).
 * Mobile: the pane collapses and ion-tabs shows a bottom tab bar. Only built screens are listed.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterLink, RouterLinkActive, IonSplitPane, IonMenu, IonContent, IonList, IonItem, IonIcon, IonLabel, IonTabs, IonTabBar, IonTabButton, TranslatePipe],
  templateUrl: './shell.component.html', styleUrl: './shell.component.scss',
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  readonly tenant = inject(TenantService);
  private readonly router = inject(Router);
  readonly offline = inject(OfflineService);
  readonly cartState = inject(CartState);
  readonly net = inject(NetworkService);
  readonly sync = inject(ProgressSyncService);
  private readonly all = [
    { path: '/app/dashboard', tab: 'dashboard', icon: 'home-outline', label: 'nav.dashboard', roles: [] as string[], mobile: true },
    { path: '/app/courses', tab: 'courses', icon: 'book-outline', label: 'nav.courses', roles: ['Student', 'Instructor'], mobile: true },
    { path: '/app/paths', tab: 'paths', icon: 'git-branch-outline', label: 'nav.paths', roles: ['Student'], mobile: false },
    { path: '/app/ai', tab: 'ai', icon: 'sparkles-outline', label: 'nav.ai', roles: ['Student'], mobile: true },
    { path: '/app/wallet', tab: 'wallet', icon: 'wallet-outline', label: 'nav.wallet', roles: ['Student'], mobile: true },
    { path: '/app/cart', tab: 'cart', icon: 'cart-outline', label: 'nav.cart', roles: ['Student'], mobile: false },
    { path: '/app/downloads', tab: 'downloads', icon: 'download-outline', label: 'nav.downloads', roles: ['Student'], mobile: false },
    { path: '/app/homework', tab: 'homework', icon: 'create-outline', label: 'nav.homework', roles: ['Student'], mobile: true },
    { path: '/app/orders', tab: 'orders', icon: 'receipt-outline', label: 'nav.orders', roles: ['Student', 'Instructor'], mobile: false },
    { path: '/app/community', tab: 'community', icon: 'chatbubbles-outline', label: 'nav.community', roles: [] as string[], mobile: false },
    { path: '/app/achievements', tab: 'achievements', icon: 'trophy-outline', label: 'nav.achievements', roles: [] as string[], mobile: false },
    { path: '/app/parent', tab: 'parent', icon: 'people-outline', label: 'nav.parent', roles: ['Parent'], mobile: false },
    { path: '/app/payments', tab: 'payments', icon: 'card-outline', label: 'nav.payments', roles: ['TenantAdmin'], mobile: true },
    { path: '/app/monetization', tab: 'monetization', icon: 'pricetags-outline', label: 'nav.monetization', roles: ['TenantAdmin'], mobile: true },
    { path: '/app/users', tab: 'users', icon: 'people-outline', label: 'nav.users', roles: ['TenantAdmin'], mobile: true },
    { path: '/app/approvals', tab: 'approvals', icon: 'checkmark-done-outline', label: 'nav.approvals', roles: ['TenantAdmin'], mobile: true },
    { path: '/app/finance', tab: 'finance', icon: 'cash-outline', label: 'nav.finance', roles: ['TenantAdmin', 'Instructor'], mobile: false },
    { path: '/app/ops', tab: 'ops', icon: 'shield-checkmark-outline', label: 'nav.ops', roles: ['TenantAdmin', 'SuperAdmin'], mobile: false },
    { path: '/app/settings', tab: 'settings', icon: 'settings-outline', label: 'nav.settings', roles: ['TenantAdmin'], mobile: false },
    { path: '/app/tenants', tab: 'tenants', icon: 'business-outline', label: 'nav.tenants', roles: ['SuperAdmin'], mobile: true },
    { path: '/app/assessments', tab: 'assessments', icon: 'help-circle-outline', label: 'nav.assessments', roles: ['Instructor'], mobile: true },
    { path: '/app/assignments', tab: 'assignments', icon: 'clipboard-outline', label: 'nav.assignments', roles: ['Instructor', 'TenantAdmin', 'TeachingAssistant'], mobile: false },
    { path: '/app/build', tab: 'build', icon: 'construct-outline', label: 'nav.build', roles: ['Instructor'], mobile: true },
    { path: '/app/notifications', tab: 'notifications', icon: 'notifications-outline', label: 'nav.notifications', roles: [] as string[], mobile: true },
    { path: '/app/profile', tab: 'profile', icon: 'person-outline', label: 'nav.profile', roles: [] as string[], mobile: true },
  ];
  /** Only screens that exist, and only the ones the signed-in roles can use (the API still enforces roles). */
  readonly items = computed(() => this.all.filter((i) => i.roles.length === 0 || this.auth.hasRole(...i.roles)));
  /** Phone bar: four primary destinations plus a More button (opens the full menu). Keeps labels readable at 320px. */
  readonly tabs = computed(() => this.items().filter((i) => i.mobile).slice(0, 4));
  private readonly menu = inject(MenuController);
  openMenu(): Promise<boolean> { return this.menu.open('sidebar'); }
  closeMenu(): Promise<boolean> { return this.menu.close('sidebar'); }
  constructor() {
    addIcons({ 'chatbubbles-outline': chatbubblesOutline, 'clipboard-outline': clipboardOutline, 'create-outline': createOutline, 'ellipsis-horizontal-outline': ellipsisHorizontalOutline, 'download-outline': downloadOutline, 'cloud-offline-outline': cloudOfflineOutline, 'pricetags-outline': pricetagsOutline, 'business-outline': businessOutline, 'people-outline': peopleOutline, 'checkmark-done-outline': checkmarkDoneOutline, 'settings-outline': settingsOutline, 'card-outline': cardOutline, 'help-circle-outline': helpCircleOutline, 'receipt-outline': receiptOutline, 'home-outline': homeOutline, 'notifications-outline': notificationsOutline, 'person-outline': personOutline, 'log-out-outline': logOutOutline, 'book-outline': bookOutline, 'git-branch-outline': gitBranchOutline, 'sparkles-outline': sparklesOutline, 'wallet-outline': walletOutline, 'cart-outline': cartOutline, 'construct-outline': constructOutline, 'trophy-outline': trophyOutline, 'cash-outline': cashOutline, 'shield-checkmark-outline': shieldCheckmarkOutline });
    // replay progress saved offline whenever the device is online and someone is signed in
    effect(() => { if (this.auth.user()) { void this.sync.refreshPending(); if (this.net.online()) void this.sync.flush(); } });
    // cart badge: load the count once per sign-in; cart add/remove flows keep it fresh
    effect(() => { if (this.auth.user() && this.auth.hasRole('Student')) void this.cartState.refresh(); });
  }
  async logout(): Promise<void> { await this.offline.clearUser(); await this.auth.logout(); await this.router.navigateByUrl('/login'); }
}
