import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';

const fs = vi.hoisted(() => ({ writeFile: vi.fn(), readFile: vi.fn(), deleteFile: vi.fn(), rmdir: vi.fn() }));
const cap = vi.hoisted(() => ({ native: true }));
vi.mock('@capacitor/filesystem', () => ({ Filesystem: fs, Directory: { Data: 'DATA' } }));
vi.mock('@capacitor/core', async (orig) => ({ ...(await orig<typeof import('@capacitor/core')>()), Capacitor: { isNativePlatform: () => cap.native } }));

import { AuthService } from './auth.service';
import { LearningService } from './learning.service';
import { StorageService } from './storage.service';
import { OfflineService } from './offline.service';
import { CourseDetail, Lesson } from '../models/learning';

class MemStore { m = new Map<string, string>(); async getJson<T>(k: string): Promise<T | null> { const v = this.m.get(k); return v ? (JSON.parse(v) as T) : null; } async setJson(k: string, v: unknown): Promise<void> { this.m.set(k, JSON.stringify(v)); } async remove(k: string): Promise<void> { this.m.delete(k); } }
const lesson = { id: 'l1', courseId: 'c1', sectionId: 's1', title: 'Intro', lessonType: 'Video', contentMarkdown: null, externalUrl: null, durationSeconds: 60, isFreePreview: false } as Lesson;
const course = { id: 'c1', title: 'Algebra', sections: [] } as unknown as CourseDetail;

function setup(native: boolean, blob = new Blob([new Uint8Array([1, 2, 3])], { type: 'video/mp4' })) {
  cap.native = native; Object.values(fs).forEach((f) => f.mockReset()); fs.writeFile.mockResolvedValue({}); fs.deleteFile.mockResolvedValue({}); fs.rmdir.mockResolvedValue({});
  const store = new MemStore();
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [{ provide: StorageService, useValue: store }, { provide: LearningService, useValue: { streamBlob: vi.fn().mockResolvedValue(blob) } }, { provide: AuthService, useValue: { user: () => ({ id: 'u1' }) } }] });
  return { svc: TestBed.inject(OfflineService), store };
}

describe('OfflineService', () => {
  beforeEach(() => undefined);
  it('refuses to download on the web', async () => {
    const { svc } = setup(false);
    expect(svc.supported).toBe(false);
    await expect(svc.download(lesson, course)).rejects.toThrow('unsupported');
    expect(fs.writeFile).not.toHaveBeenCalled();
  });
  it('writes the file under the user folder in the private data directory and records the entry', async () => {
    const { svc, store } = setup(true);
    await svc.download(lesson, course);
    expect(fs.writeFile).toHaveBeenCalledWith(expect.objectContaining({ path: 'offline/u1/l1', directory: 'DATA', recursive: true }));
    expect(svc.entry('l1')).toMatchObject({ title: 'Intro', sizeBytes: 3, mimeType: 'video/mp4' });
    expect(store.m.has('offline.manifest.u1')).toBe(true);
    expect(svc.usedBytes()).toBe(3);
  });
  it('rejects a file over the size limit before writing it', async () => {
    const big = { size: 400 * 1024 * 1024, type: 'video/mp4' } as Blob;
    const { svc } = setup(true, big);
    await expect(svc.download(lesson, course)).rejects.toThrow('too-large');
    expect(fs.writeFile).not.toHaveBeenCalled();
  });
  it('opens a saved copy and removes an expired one', async () => {
    const { svc } = setup(true);
    await svc.download(lesson, course);
    fs.readFile.mockResolvedValue({ data: 'AQID' });
    expect((await svc.open('l1')).size).toBe(3);
    // age the entry past the limit
    const old = { ...svc.entry('l1')!, downloadedAtUtc: new Date(Date.now() - 20 * 86400000).toISOString() };
    svc.entries.set([old]);
    await expect(svc.open('l1')).rejects.toThrow('expired');
    expect(svc.entry('l1')).toBeNull();
    expect(fs.deleteFile).toHaveBeenCalled();
  });
  it('drops the entry when the file is gone', async () => {
    const { svc } = setup(true);
    await svc.download(lesson, course);
    fs.readFile.mockRejectedValue(new Error('gone'));
    await expect(svc.open('l1')).rejects.toThrow('missing');
    expect(svc.entry('l1')).toBeNull();
  });
  it('clearUser removes the user folder and manifest', async () => {
    const { svc, store } = setup(true);
    await svc.download(lesson, course);
    await svc.clearUser();
    expect(fs.rmdir).toHaveBeenCalledWith(expect.objectContaining({ path: 'offline/u1', recursive: true }));
    expect(store.m.has('offline.manifest.u1')).toBe(false);
    expect(svc.entries()).toEqual([]);
  });
});
