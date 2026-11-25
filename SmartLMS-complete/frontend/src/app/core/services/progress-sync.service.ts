import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';
import { LearningService } from './learning.service';
import { NetworkService } from './network.service';
import { StorageService } from './storage.service';
import { QueuedProgress, RejectedProgress, coalesce, isRetryable } from '../util/offline';

/**
 * Lesson progress with an offline queue. Online it saves straight away. Offline (or on a network error) it queues one entry
 * per lesson on the device, per signed-in user, and replays them in order when the connection is back.
 * Each replay carries the time it was recorded so the server never moves the position back to an older value.
 */
@Injectable({ providedIn: 'root' })
export class ProgressSyncService {
  private readonly api = inject(LearningService);
  private readonly net = inject(NetworkService);
  private readonly store = inject(StorageService);
  private readonly auth = inject(AuthService);
  private flushing: Promise<{ sent: number; dropped: number }> | null = null;
  readonly pending = signal(0);
  /** Entries the server refused for good (not enrolled, lesson gone). Kept until the user dismisses them so progress is never lost silently. */
  readonly rejected = signal<RejectedProgress[]>([]);

  private key(): string | null { const id = this.auth.user()?.id; return id ? `sync.progress.${id}` : null; }
  private async read(): Promise<QueuedProgress[]> { const k = this.key(); return k ? (await this.store.getJson<QueuedProgress[]>(k)) ?? [] : []; }
  private async write(q: QueuedProgress[]): Promise<void> { const k = this.key(); if (!k) return; await this.store.setJson(k, q); this.pending.set(q.length); }

  private rkey(): string | null { const id = this.auth.user()?.id; return id ? `sync.rejected.${id}` : null; }
  private async reject(lessonId: string, status: number | undefined): Promise<void> {
    const k = this.rkey(); if (!k) return;
    const list = [...((await this.store.getJson<RejectedProgress[]>(k)) ?? []), { lessonId, status: status ?? 0, atUtc: new Date().toISOString() }].slice(-50);
    await this.store.setJson(k, list); this.rejected.set(list);
  }
  async dismissRejected(): Promise<void> { const k = this.rkey(); if (k) await this.store.setJson(k, []); this.rejected.set([]); }

  async refreshPending(): Promise<void> {
    this.pending.set((await this.read()).length);
    const k = this.rkey(); this.rejected.set(k ? (await this.store.getJson<RejectedProgress[]>(k)) ?? [] : []);
  }

  /** Saves a heartbeat now, or queues it. Never throws for connectivity problems. */
  async record(lessonId: string, position: number, delta: number, completed: boolean, speed: number | null): Promise<'saved' | 'queued'> {
    const item: QueuedProgress = { lessonId, lastPositionSeconds: Math.max(0, Math.floor(position)), watchedSecondsDelta: delta, completed, playbackSpeed: speed, recordedAtUtc: new Date().toISOString() };
    if (this.net.online() && (await this.read()).length === 0) {
      try { await this.api.saveProgress(lessonId, item.lastPositionSeconds, delta, completed, speed ?? 1); return 'saved'; }
      catch (e) { const st = (e as { status?: number }).status; if (!isRetryable(st)) { await this.reject(lessonId, st); return 'saved'; } }
    }
    await this.write(coalesce(await this.read(), item));
    if (this.net.online()) void this.flush(); // older entries are waiting: drain them in order
    return 'queued';
  }

  /** Replays the queue in order. Stops at the first retryable failure; drops entries the server will never accept. */
  flush(): Promise<{ sent: number; dropped: number }> {
    if (this.flushing) return this.flushing;
    this.flushing = this.run().finally(() => { this.flushing = null; });
    return this.flushing;
  }
  private async run(): Promise<{ sent: number; dropped: number }> {
    let sent = 0, dropped = 0;
    if (!this.net.online()) return { sent, dropped };
    let queue = await this.read();
    while (queue.length > 0) {
      const q = queue[0];
      try {
        await this.api.saveProgress(q.lessonId, q.lastPositionSeconds, q.watchedSecondsDelta, q.completed, q.playbackSpeed ?? 1, q.recordedAtUtc);
        sent++;
      } catch (e) {
        const st = (e as { status?: number }).status;
        if (isRetryable(st)) break;
        await this.reject(q.lessonId, st);
        dropped++;
      }
      // re-read: the player may have queued a newer heartbeat while the request was in flight. Only the entry we sent is removed.
      queue = (await this.read()).filter((f) => !(f.lessonId === q.lessonId && f.recordedAtUtc === q.recordedAtUtc));
      await this.write(queue); // saved after every entry, so a crash or a lost connection never resends what already went through
    }
    await this.refreshPending();
    return { sent, dropped };
  }
}
