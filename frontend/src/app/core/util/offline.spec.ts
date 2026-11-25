import { describe, it, expect } from 'vitest';
import { MAX_DELTA_SECONDS, OFFLINE_MAX_DAYS, QueuedProgress, base64ToBlob, blobToBase64, coalesce, expiresOn, formatBytes, isExpired, isRetryable } from './offline';

const q = (o: Partial<QueuedProgress> = {}): QueuedProgress => ({ lessonId: 'l', lastPositionSeconds: 1, watchedSecondsDelta: 10, completed: false, playbackSpeed: 1, recordedAtUtc: '2026-01-01T00:00:00Z', ...o });

describe('offline helpers', () => {
  it('coalesce keeps the latest position, sums and caps watched time, and completion sticks', () => {
    const a = coalesce([], q({ watchedSecondsDelta: 200, completed: true }));
    const b = coalesce(a, q({ lastPositionSeconds: 90, watchedSecondsDelta: 200, completed: false, recordedAtUtc: '2026-01-01T00:01:00Z' }));
    expect(b).toHaveLength(1);
    expect(b[0]).toMatchObject({ lastPositionSeconds: 90, watchedSecondsDelta: MAX_DELTA_SECONDS, completed: true, recordedAtUtc: '2026-01-01T00:01:00Z' });
    expect(coalesce(b, q({ lessonId: 'other' }))).toHaveLength(2);
  });
  it('coalesce ignores negative watched time', () => { expect(coalesce([], q({ watchedSecondsDelta: -50 }))[0].watchedSecondsDelta).toBe(0); });
  it('retries network and server errors only', () => {
    for (const s of [undefined, 0, 401, 408, 429, 500, 503]) expect(isRetryable(s)).toBe(true);
    for (const s of [400, 403, 404, 409]) expect(isRetryable(s)).toBe(false);
  });
  it('an offline copy expires after the limit and a bad date counts as expired', () => {
    const now = new Date('2026-02-01T00:00:00Z');
    expect(isExpired('2026-01-25T00:00:00Z', now)).toBe(false);
    expect(isExpired(new Date(now.getTime() - (OFFLINE_MAX_DAYS + 1) * 86400000).toISOString(), now)).toBe(true);
    expect(isExpired('nonsense', now)).toBe(true);
    expect(expiresOn('2026-01-01T00:00:00Z').toISOString()).toBe('2026-01-15T00:00:00.000Z');
  });
  it('base64 and blob round trip keeps the bytes and type', async () => {
    const bytes = new Uint8Array([0, 1, 2, 250, 255]);
    const b64 = await blobToBase64(new Blob([bytes], { type: 'video/mp4' }));
    const back = base64ToBlob(b64, 'video/mp4');
    expect(back.type).toBe('video/mp4');
    expect(new Uint8Array(await back.arrayBuffer())).toEqual(bytes);
  });
  it('formats sizes', () => { expect(formatBytes(500)).toBe('500 B'); expect(formatBytes(2048)).toBe('2 KB'); expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB'); });
});
