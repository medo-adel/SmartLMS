import { Injectable, inject, signal } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { Directory, Filesystem } from '@capacitor/filesystem';
import { AuthService } from './auth.service';
import { LearningService } from './learning.service';
import { StorageService } from './storage.service';
import { CourseDetail, Lesson } from '../models/learning';
import { MAX_DOWNLOAD_BYTES, base64ToBlob, blobToBase64, isExpired } from '../util/offline';

export interface OfflineEntry {
  lessonId: string; courseId: string; title: string; lessonType: string; mimeType: string; sizeBytes: number;
  path: string; downloadedAtUtc: string; lesson: Lesson; course: CourseDetail;
}
export type OfflineError = 'unsupported' | 'too-large' | 'expired' | 'missing' | 'no-user';

/**
 * Offline lesson downloads through Capacitor Filesystem (the app's private Data directory, not shared storage).
 * Only available in the native app. The lesson text and course outline are saved with the file so the player can open it offline.
 * Offline copies are not encrypted by this app and stop working after 14 days, then the server checks access again.
 */
@Injectable({ providedIn: 'root' })
export class OfflineService {
  private readonly api = inject(LearningService);
  private readonly store = inject(StorageService);
  private readonly auth = inject(AuthService);
  readonly supported = Capacitor.isNativePlatform();
  readonly entries = signal<OfflineEntry[]>([]);
  readonly busy = signal<string>('');

  private uid(): string | null { return this.auth.user()?.id ?? null; }
  private key(uid: string): string { return `offline.manifest.${uid}`; }
  private fail(code: OfflineError): never { throw new Error(code); }

  async load(): Promise<void> {
    const uid = this.uid();
    this.entries.set(uid ? (await this.store.getJson<OfflineEntry[]>(this.key(uid))) ?? [] : []);
  }
  entry(lessonId: string): OfflineEntry | null { return this.entries().find((e) => e.lessonId === lessonId) ?? null; }
  usedBytes(): number { return this.entries().reduce((n, e) => n + e.sizeBytes, 0); }

  async download(lesson: Lesson, course: CourseDetail): Promise<void> {
    if (!this.supported) this.fail('unsupported');
    const uid = this.uid() ?? this.fail('no-user');
    this.busy.set(lesson.id);
    try {
      const blob = await this.api.streamBlob(lesson.id);
      if (blob.size > MAX_DOWNLOAD_BYTES) this.fail('too-large');
      const path = `offline/${uid}/${lesson.id}`;
      await Filesystem.writeFile({ path, data: await blobToBase64(blob), directory: Directory.Data, recursive: true });
      const entry: OfflineEntry = { lessonId: lesson.id, courseId: lesson.courseId, title: lesson.title, lessonType: lesson.lessonType, mimeType: blob.type || 'application/octet-stream', sizeBytes: blob.size, path, downloadedAtUtc: new Date().toISOString(), lesson, course };
      await this.save([...this.entries().filter((e) => e.lessonId !== lesson.id), entry]);
    } finally { this.busy.set(''); }
  }

  /** Reads a downloaded file back as a Blob. An expired or missing copy is removed and reported. */
  async open(lessonId: string): Promise<Blob> {
    const e = this.entry(lessonId) ?? this.fail('missing');
    if (isExpired(e.downloadedAtUtc)) { await this.remove(lessonId); this.fail('expired'); }
    try {
      const r = await Filesystem.readFile({ path: e.path, directory: Directory.Data });
      return base64ToBlob(typeof r.data === 'string' ? r.data : await blobToBase64(r.data), e.mimeType);
    } catch { await this.remove(lessonId); this.fail('missing'); }
  }

  async remove(lessonId: string): Promise<void> {
    const e = this.entry(lessonId); if (!e) return;
    try { await Filesystem.deleteFile({ path: e.path, directory: Directory.Data }); } catch { /* already gone */ }
    await this.save(this.entries().filter((x) => x.lessonId !== lessonId));
  }

  /** Deletes every downloaded file and the manifest of the signed-in user. Called on sign out so a shared device keeps nothing. */
  async clearUser(): Promise<void> {
    const uid = this.uid(); if (!uid) return;
    try { await Filesystem.rmdir({ path: `offline/${uid}`, directory: Directory.Data, recursive: true }); } catch { /* nothing downloaded */ }
    await this.store.remove(this.key(uid));
    this.entries.set([]);
  }

  private async save(list: OfflineEntry[]): Promise<void> {
    const uid = this.uid() ?? this.fail('no-user');
    await this.store.setJson(this.key(uid), list);
    this.entries.set(list);
  }
}
