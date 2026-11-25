import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonContent, IonButton } from '@ionic/angular';
import { OfflineService } from '../../core/services/offline.service';
import { NetworkService } from '../../core/services/network.service';
import { ProgressSyncService } from '../../core/services/progress-sync.service';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { expiresOn, formatBytes, isExpired } from '../../core/util/offline';

/** Offline downloads of this device, storage used, and the progress waiting to sync. */
@Component({ selector: 'app-downloads', imports: [DatePipe, RouterLink, IonContent, IonButton, TranslatePipe], templateUrl: './downloads.page.html', styleUrl: './downloads.page.scss' })
export class DownloadsPage implements OnInit {
  readonly offline = inject(OfflineService);
  readonly net = inject(NetworkService);
  readonly sync = inject(ProgressSyncService);
  readonly result = signal('');
  /** Refused entries grouped by cause, so the notice says what actually happened instead of guessing one reason. */
  readonly reasons = computed(() => {
    const key = (st: number): string => (st === 403 ? 'dl.reason403' : st === 404 ? 'dl.reason404' : 'dl.reasonOther');
    const m = new Map<string, number>();
    for (const x of this.sync.rejected()) m.set(key(x.status), (m.get(key(x.status)) ?? 0) + 1);
    return [...m].map(([k, count]) => ({ k, count }));
  });
  readonly bytes = formatBytes;
  readonly expiry = expiresOn;
  readonly expired = (iso: string): boolean => isExpired(iso);

  async ngOnInit(): Promise<void> { await Promise.all([this.offline.load(), this.sync.refreshPending()]); }
  async syncNow(): Promise<void> {
    const r = await this.sync.flush();
    this.result.set(r.sent === 0 && r.dropped === 0 ? 'dl.nothingSent' : 'dl.synced');
  }
  async remove(id: string): Promise<void> { await this.offline.remove(id); }
}
