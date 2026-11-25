import { Component, OnChanges, SimpleChanges, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonButton } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { LessonBookmark, LessonChapter, LessonNote, LessonResource } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

export function clock(sec: number): string { const s = Math.max(0, Math.floor(sec)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; }

/** Chapters, timestamped notes, bookmarks and downloadable resources for the open lesson. */
@Component({ selector: 'app-lesson-extras', imports: [FormsModule, IonButton, TranslatePipe], templateUrl: './lesson-extras.component.html', styleUrl: './lesson-extras.component.scss' })
export class LessonExtrasComponent implements OnChanges {
  private readonly api = inject(LearningService);
  readonly lessonId = input.required<string>();
  readonly timed = input(false);
  readonly now = input<() => number>(() => 0);
  readonly seek = output<number>();
  readonly chapters = signal<LessonChapter[]>([]);
  readonly notes = signal<LessonNote[]>([]);
  readonly marks = signal<LessonBookmark[]>([]);
  readonly files = signal<LessonResource[]>([]);
  readonly failed = signal(false);
  body = '';
  readonly clock = clock;

  ngOnChanges(c: SimpleChanges): void { if (c['lessonId']) void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false); const id = this.lessonId();
    const [ch, n, b, f] = await Promise.allSettled([this.api.chapters(id), this.api.notes(id), this.api.bookmarks(id), this.api.resources(id)]);
    this.chapters.set(ch.status === 'fulfilled' ? ch.value : []); this.notes.set(n.status === 'fulfilled' ? n.value : []);
    this.marks.set(b.status === 'fulfilled' ? b.value : []); this.files.set(f.status === 'fulfilled' ? f.value : []);
    if ([ch, n, b, f].every((x) => x.status === 'rejected')) this.failed.set(true);
  }
  async addNote(): Promise<void> {
    const text = this.body.trim(); if (!text) return;
    try { await this.api.addNote(this.lessonId(), this.timed() ? Math.floor(this.now()()) : null, text); this.body = ''; this.notes.set(await this.api.notes(this.lessonId())); } catch { /* toast */ }
  }
  editingNote: string | null = null; editBody = '';
  startEditNote(n: LessonNote): void { this.editingNote = n.id; this.editBody = n.body; }
  async saveNote(n: LessonNote): Promise<void> {
    const text = this.editBody.trim(); if (!text) return;
    try { await this.api.editNote(n.id, n.timestampSeconds, text); this.editingNote = null; this.notes.set(await this.api.notes(this.lessonId())); } catch { /* toast */ }
  }
  async removeNote(id: string): Promise<void> { try { await this.api.deleteNote(id); this.notes.set(this.notes().filter((x) => x.id !== id)); } catch { /* toast */ } }
  async addMark(): Promise<void> {
    try { await this.api.addBookmark(this.lessonId(), Math.floor(this.now()()), null); this.marks.set(await this.api.bookmarks(this.lessonId())); } catch { /* toast */ }
  }
  async removeMark(id: string): Promise<void> { try { await this.api.deleteBookmark(id); this.marks.set(this.marks().filter((x) => x.id !== id)); } catch { /* toast */ } }
  async download(r: LessonResource): Promise<void> {
    try {
      const blob = await this.api.resourceBlob(this.lessonId(), r.id); const a = document.createElement('a');
      a.href = URL.createObjectURL(blob); a.download = r.fileName || r.title; a.click(); URL.revokeObjectURL(a.href);
    } catch { /* toast */ }
  }
  size(b: number): string { return b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`; }
}
