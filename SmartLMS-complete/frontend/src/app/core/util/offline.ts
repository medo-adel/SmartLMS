/** Pure helpers for the offline features, kept apart from the Capacitor calls so they are easy to test. */

export const OFFLINE_MAX_DAYS = 14;
export const MAX_DOWNLOAD_BYTES = 300 * 1024 * 1024;
export const MAX_DELTA_SECONDS = 300;

export interface QueuedProgress {
  lessonId: string;
  lastPositionSeconds: number;
  watchedSecondsDelta: number;
  completed: boolean;
  playbackSpeed: number | null;
  recordedAtUtc: string;
}

/** A progress entry the server refused for good (HTTP 4xx other than 401/408/429). Shown to the user instead of being dropped silently. */
export interface RejectedProgress { lessonId: string; status: number; atUtc: string; }

/** Merges a new heartbeat into the queue: one entry per lesson, latest position, summed (capped) watched time, completion sticks. */
export function coalesce(queue: readonly QueuedProgress[], item: QueuedProgress): QueuedProgress[] {
  const i = queue.findIndex((q) => q.lessonId === item.lessonId);
  if (i < 0) return [...queue, { ...item, watchedSecondsDelta: Math.min(MAX_DELTA_SECONDS, Math.max(0, item.watchedSecondsDelta)) }];
  const old = queue[i];
  const merged: QueuedProgress = {
    ...item,
    watchedSecondsDelta: Math.min(MAX_DELTA_SECONDS, Math.max(0, old.watchedSecondsDelta) + Math.max(0, item.watchedSecondsDelta)),
    completed: old.completed || item.completed,
  };
  return queue.map((q, k) => (k === i ? merged : q));
}

/** Network failures and server trouble are retried later. Other client errors (not enrolled, lesson gone) are dropped so the queue cannot jam. */
export function isRetryable(status: number | undefined): boolean {
  // 401: the session expired and could not be refreshed. The entry stays queued and is sent after the user signs in again.
  return status === undefined || status === 0 || status === 401 || status === 408 || status === 429 || status >= 500;
}

/** An offline copy stops working after OFFLINE_MAX_DAYS so access (enrolment, expiry) is checked again by the server. */
export function isExpired(downloadedAtUtc: string, now: Date = new Date()): boolean {
  const t = Date.parse(downloadedAtUtc);
  return Number.isNaN(t) || now.getTime() - t > OFFLINE_MAX_DAYS * 86_400_000;
}

export function expiresOn(downloadedAtUtc: string): Date { return new Date(Date.parse(downloadedAtUtc) + OFFLINE_MAX_DAYS * 86_400_000); }

export function base64ToBlob(b64: string, type: string): Blob {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return new Blob([bytes], { type });
}

export function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const r = new FileReader();
    r.onerror = () => reject(r.error ?? new Error('read failed'));
    r.onload = () => { const s = String(r.result); resolve(s.slice(s.indexOf(',') + 1)); };
    r.readAsDataURL(blob);
  });
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} KB`;
  return `${(n / 1024 / 1024).toFixed(1)} MB`;
}
