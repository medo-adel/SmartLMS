import { Component, inject, signal, OnInit, computed } from '@angular/core';
import { DatePipe } from '@angular/common';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { ApiService } from '../../core/services/api.service';
import { AppNotification } from '../../core/models/models';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

@Component({
  selector: 'app-notifications', imports: [DatePipe, IonContent, IonButton, IonSpinner, TranslatePipe],
  templateUrl: './notifications.page.html', styleUrl: './notifications.page.scss',
})
export class NotificationsPage implements OnInit {
  private readonly api = inject(ApiService);
  readonly items = signal<AppNotification[] | null>(null);
  readonly failed = signal(false);
  readonly unread = computed(() => (this.items() ?? []).filter((n) => !n.isRead).length);

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { this.items.set(await this.api.notifications()); } catch { this.failed.set(true); }
  }
  async markRead(n: AppNotification): Promise<void> {
    try { await this.api.markRead(n.id); this.items.update((l) => (l ?? []).map((x) => (x.id === n.id ? { ...x, isRead: true } : x))); } catch { /* toast shown by interceptor */ }
  }
}
