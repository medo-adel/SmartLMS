import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { AuthService } from './auth.service';
import { LearningService } from './learning.service';
import { NetworkService } from './network.service';
import { StorageService } from './storage.service';
import { ProgressSyncService } from './progress-sync.service';
import { QueuedProgress } from '../util/offline';

class MemStore { m = new Map<string, string>(); async getJson<T>(k: string): Promise<T | null> { const v = this.m.get(k); return v ? (JSON.parse(v) as T) : null; } async setJson(k: string, v: unknown): Promise<void> { this.m.set(k, JSON.stringify(v)); } }

describe('ProgressSyncService', () => {
  let svc: ProgressSyncService, online: WritableSignal<boolean>, store: MemStore, save: ReturnType<typeof vi.fn>, uid: WritableSignal<string>;
  beforeEach(() => {
    online = signal<boolean>(true); store = new MemStore(); save = vi.fn().mockResolvedValue({}); uid = signal<string>('u1');
    TestBed.configureTestingModule({ providers: [
      { provide: NetworkService, useValue: { online } }, { provide: StorageService, useValue: store },
      { provide: LearningService, useValue: { saveProgress: save } }, { provide: AuthService, useValue: { user: () => ({ id: uid() }) } },
    ] });
    svc = TestBed.inject(ProgressSyncService);
  });
  const queued = async (id = 'u1') => (await store.getJson<QueuedProgress[]>(`sync.progress.${id}`)) ?? [];

  it('saves straight away when online and nothing is waiting', async () => {
    expect(await svc.record('l1', 30, 15, false, 1)).toBe('saved');
    expect(save).toHaveBeenCalledWith('l1', 30, 15, false, 1);
    expect(await queued()).toEqual([]);
  });
  it('queues one coalesced entry per lesson while offline and never calls the API', async () => {
    online.set(false);
    await svc.record('l1', 10, 15, false, 1); await svc.record('l1', 55, 15, false, 1.25); await svc.record('l2', 5, 5, true, 1);
    const q = await queued();
    expect(save).not.toHaveBeenCalled();
    expect(q).toHaveLength(2);
    expect(q[0]).toMatchObject({ lessonId: 'l1', lastPositionSeconds: 55, watchedSecondsDelta: 30, playbackSpeed: 1.25 });
    expect(svc.pending()).toBe(2);
  });
  it('queues when the network fails mid request, but not when the server says the request is invalid', async () => {
    save.mockRejectedValue({ status: 0 }); // the connection stays down
    expect(await svc.record('l1', 5, 5, false, 1)).toBe('queued');
    await svc.flush();
    expect(await queued()).toHaveLength(1);
    save.mockResolvedValue({});
    await svc.flush(); // connection back: replays and clears it
    expect(await queued()).toHaveLength(0);
    save.mockRejectedValueOnce({ status: 403 });
    expect(await svc.record('l9', 5, 5, false, 1)).toBe('saved'); // dropped: not enrolled, retrying cannot help
    expect(await queued()).toHaveLength(0);
    expect(svc.rejected().map((x) => [x.lessonId, x.status])).toEqual([['l9', 403]]); // and the user can see it
  });
  it('keeps progress queued when the session expired (401) and resends the identical recorded time on retry', async () => {
    online.set(false);
    await svc.record('a', 10, 12, false, 1);
    const first = (await queued())[0];
    online.set(true);
    save.mockRejectedValueOnce({ status: 401 });
    expect(await svc.flush()).toEqual({ sent: 0, dropped: 0 });
    expect(await queued()).toHaveLength(1); // not lost
    save.mockRejectedValueOnce({ status: 0 }); // response lost after the server may have applied it
    await svc.flush();
    expect(await queued()).toHaveLength(1);
    save.mockResolvedValue({});
    await svc.flush();
    const times = save.mock.calls.filter((c) => c[0] === 'a').map((c) => c[5]);
    expect(times.length).toBeGreaterThanOrEqual(3);
    expect(new Set(times)).toEqual(new Set([first.recordedAtUtc])); // same timestamp every time: the server uses it to ignore a repeat
    expect(await queued()).toHaveLength(0);
  });
  it('records a refused entry during a flush, keeps it across reloads and clears it on dismiss', async () => {
    online.set(false);
    await svc.record('gone', 1, 1, false, 1); await svc.record('ok', 2, 2, false, 1);
    online.set(true);
    save.mockRejectedValueOnce({ status: 404 }).mockResolvedValue({});
    expect(await svc.flush()).toEqual({ sent: 1, dropped: 1 });
    svc.rejected.set([]); await svc.refreshPending(); // as after an app restart
    expect(svc.rejected().map((x) => x.lessonId)).toEqual(['gone']);
    await svc.dismissRejected();
    await svc.refreshPending();
    expect(svc.rejected()).toEqual([]);
  });
  it('flush replays in order with the recorded time, removes sent entries and keeps entries after a retryable failure', async () => {
    online.set(false);
    await svc.record('a', 1, 1, false, 1); await svc.record('b', 2, 2, false, 1); await svc.record('c', 3, 3, true, 1);
    online.set(true);
    save.mockResolvedValueOnce({}).mockRejectedValueOnce({ status: 503 });
    const r = await svc.flush();
    expect(r).toEqual({ sent: 1, dropped: 0 });
    expect(save.mock.calls[0][0]).toBe('a'); expect(save.mock.calls[0][5]).toMatch(/^\d{4}-\d\d-\d\dT/);
    expect((await queued()).map((x) => x.lessonId)).toEqual(['b', 'c']);
    save.mockResolvedValue({});
    expect(await svc.flush()).toEqual({ sent: 2, dropped: 0 });
    expect(await queued()).toEqual([]);
    expect(svc.pending()).toBe(0);
  });
  it('drops an entry the server refuses for good so the queue cannot jam', async () => {
    online.set(false); await svc.record('gone', 1, 1, false, 1); await svc.record('ok', 2, 2, false, 1); online.set(true);
    save.mockRejectedValueOnce({ status: 404 }).mockResolvedValueOnce({});
    expect(await svc.flush()).toEqual({ sent: 1, dropped: 1 });
    expect(await queued()).toEqual([]);
  });
  it('does nothing while offline and keeps each user queue separate', async () => {
    online.set(false); await svc.record('l1', 1, 1, false, 1);
    expect(await svc.flush()).toEqual({ sent: 0, dropped: 0 });
    uid.set('u2');
    expect(await queued('u2')).toEqual([]);
    expect(await queued('u1')).toHaveLength(1);
  });
});
