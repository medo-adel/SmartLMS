import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { I18nService } from '../../core/services/i18n.service';
import { AiStudyPlan, AiVoice, Enrollment } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Voice tutor (browser microphone via MediaRecorder) and study planner. Both call the .NET API only. */
@Component({ selector: 'app-ai-studio', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './ai-studio.page.html', styleUrl: './ai-studio.page.scss' })
export class AiStudioPage implements OnInit {
  private readonly api = inject(LearningService);
  private readonly i18n = inject(I18nService);
  readonly courses = signal<Enrollment[] | null>(null);
  readonly failed = signal(false);
  readonly recording = signal(false);
  readonly busy = signal(false);
  readonly micError = signal(false);
  readonly voice = signal<AiVoice | null>(null);
  readonly plan = signal<AiStudyPlan | null>(null);
  courseId = '';
  target = '';
  hours = 6;
  private rec: MediaRecorder | null = null;
  private chunks: Blob[] = [];

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try { const list = (await this.api.myEnrollments()).filter((e) => e.status === 'Active'); this.courses.set(list); if (list.length) this.courseId = list[0].courseId; } catch { this.failed.set(true); }
  }
  async record(): Promise<void> {
    this.micError.set(false);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      this.chunks = []; this.rec = new MediaRecorder(stream);
      this.rec.ondataavailable = (e) => this.chunks.push(e.data);
      this.rec.onstop = () => { stream.getTracks().forEach((t) => t.stop()); void this.send(new Blob(this.chunks, { type: this.rec?.mimeType || 'audio/webm' })); };
      this.rec.start(); this.recording.set(true);
    } catch { this.micError.set(true); }
  }
  stop(): void { this.rec?.stop(); this.recording.set(false); }
  private async send(blob: Blob): Promise<void> {
    this.busy.set(true); this.voice.set(null);
    try { this.voice.set(await this.api.voiceTutor(this.courseId, blob, this.i18n.lang())); } catch { /* toast */ }
    this.busy.set(false);
  }
  audioSrc(v: AiVoice): string | null { return v.audioBase64 ? `data:${v.audioMime ?? 'audio/wav'};base64,${v.audioBase64}` : null; }
  async makePlan(): Promise<void> {
    if (!this.target) return;
    this.busy.set(true); this.plan.set(null);
    try { this.plan.set(await this.api.studyPlan(this.courseId, new Date(this.target).toISOString(), this.hours)); } catch { /* toast */ }
    this.busy.set(false);
  }
}
