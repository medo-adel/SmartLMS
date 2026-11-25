import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { AuthService } from '../../core/services/auth.service';
import { TenantService } from '../../core/services/tenant.service';
import { LearningService } from '../../core/services/learning.service';
import { CommunityService } from '../../core/services/community.service';
import { Enrollment, MyCourse } from '../../core/models/learning';
import { AnnouncementRow, ChatMessage, ChatRoom, LiveAttendanceRow, LiveSessionRow, ThreadDetail, ThreadRow } from '../../core/models/community';

/** Community: per-course discussion threads (all course members) and announcements (staff post, everyone reads). */
@Component({ selector: 'app-community', imports: [DatePipe, FormsModule, RouterLink, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './community.page.html', styleUrl: './community.page.scss' })
export class CommunityPage implements OnInit {
  private readonly learn = inject(LearningService);
  private readonly api = inject(CommunityService);
  readonly auth = inject(AuthService);
  private readonly tenant = inject(TenantService);
  /** SuperAdmin without a selected academy: community is tenant-scoped, so require an academy context first. */
  readonly needsAcademy = computed(() => this.auth.hasRole('SuperAdmin') && !this.tenant.tenantKey());
  /** Set when the backend rejects the current academy context (expired/revoked grant, inactive/unknown academy). */
  readonly contextLost = signal(false);
  /** No usable academy context: never selected, or the backend rejected it. */
  readonly noContext = computed(() => this.needsAcademy() || this.contextLost());
  private onLoadError(err: unknown, fallback: () => void): void {
    const code = err instanceof HttpErrorResponse ? err.error?.code : null;
    if (code === 'SUPPORT_ACCESS_REQUIRED' || code === 'TENANT_REQUIRED' || code === 'TENANT_MISMATCH' || code === 'TENANT_INACTIVE' || code === 'TENANT_NOT_FOUND') this.contextLost.set(true);
    else fallback();
  }

  readonly tab = signal<'threads' | 'announcements' | 'chat' | 'live'>('threads');
  readonly courses = signal<{ id: string; title: string }[] | null>(null);
  readonly failed = signal(false);
  readonly threads = signal<ThreadRow[]>([]);
  readonly openThread = signal<ThreadDetail | null>(null);
  readonly threadFailed = signal(false);
  readonly announcements = signal<AnnouncementRow[]>([]);
  readonly msg = signal('');
  course = '';
  newTitle = ''; newBody = '';
  replyBody = '';
  annTitle = ''; annBody = ''; annCourse = '';

  /** Post/reply/upvote = Student/Instructor/TA; TenantAdmin reads. Pin = staff (Grd). Announce = staff. */
  readonly canPost = computed(() => this.auth.hasRole('Student', 'Instructor', 'TeachingAssistant'));
  readonly isStaff = computed(() => this.auth.hasRole('TenantAdmin', 'Instructor', 'TeachingAssistant'));

  // ---- chat ----
  readonly rooms = signal<ChatRoom[] | null>(null);
  readonly openRoom = signal<ChatRoom | null>(null);
  readonly messages = signal<ChatMessage[]>([]);
  readonly nextBefore = signal<string | null>(null);
  chatBody = '';

  // ---- live sessions ----
  readonly live = signal<LiveSessionRow[]>([]);
  /** Join link returned by POST /join. Shown as an explicit user-tapped action because window.open after an async call is popup-blocked. */
  readonly pendingJoin = signal<{ id: string; url: string } | null>(null);
  readonly attendance = signal<{ id: string; rows: LiveAttendanceRow[] } | null>(null);
  liveCourse = '';
  lvTitle = ''; lvProvider = 'Zoom'; lvUrl = ''; lvStart = ''; lvEnd = '';
  readonly providers = ['Zoom', 'GoogleMeet', 'Jitsi'];

  ngOnInit(): void { if (this.needsAcademy()) return; void this.init(); }
  private async init(): Promise<void> {
    try {
      this.announcements.set(await this.api.announcements());
      // Students pick from enrollments; staff pick from the courses they manage.
      this.courses.set(this.auth.hasRole('Instructor', 'TenantAdmin')
        ? (await this.learn.myCourses()).map((c: MyCourse) => ({ id: c.id, title: c.title }))
        : (await this.learn.myEnrollments()).map((e: Enrollment) => ({ id: e.courseId, title: e.title })));
    } catch (e) { this.onLoadError(e, () => this.failed.set(true)); }
    if (this.contextLost()) return;
    this.loadRooms();
  }

  // ---- chat ----
  async loadRooms(): Promise<void> {
    try { this.rooms.set(await this.api.rooms()); } catch (e) { this.onLoadError(e, () => this.rooms.set([])); }
  }

  async openChat(r: ChatRoom): Promise<void> {
    if (this.openRoom()?.id === r.id) { this.openRoom.set(null); return; }
    this.openRoom.set(r); this.messages.set([]); this.nextBefore.set(null); this.chatBody = '';
    try {
      const page = await this.api.messages(r.id);
      this.messages.set([...page.messages].reverse());
      this.nextBefore.set(page.nextBefore);
    } catch { this.msg.set('common.error'); }
  }

  async loadOlder(): Promise<void> {
    const r = this.openRoom(); const before = this.nextBefore();
    if (!r || !before) return;
    try {
      const page = await this.api.messages(r.id, before);
      this.messages.set([...[...page.messages].reverse(), ...this.messages()]);
      this.nextBefore.set(page.nextBefore);
    } catch { /* toast */ }
  }

  async sendChat(): Promise<void> {
    const r = this.openRoom(); const body = this.chatBody.trim();
    if (!r || !body) return;
    try {
      await this.api.sendMessage(r.id, { body });
      this.chatBody = '';
      const page = await this.api.messages(r.id);
      const fresh = [...page.messages].reverse();
      const freshIds = new Set(fresh.map((m) => m.id));
      const oldestFresh = fresh[0]?.sentAtUtc;
      // Keep older pages already loaded (dedupe by id) instead of dropping them on the refetch.
      const older = this.messages().filter((m) => !freshIds.has(m.id) && (!oldestFresh || m.sentAtUtc < oldestFresh));
      this.messages.set([...older, ...fresh]);
      // Keep the existing older-page cursor if we already paged back; otherwise adopt the fresh page's cursor.
      if (this.nextBefore() === null && page.nextBefore && older.length === 0 && this.messages().length === fresh.length) this.nextBefore.set(page.nextBefore);
    } catch { /* toast */ }
  }

  // ---- live sessions ----
  async pickLiveCourse(): Promise<void> {
    this.live.set([]); this.attendance.set(null); this.pendingJoin.set(null);
    if (!this.liveCourse) return;
    try { this.live.set(await this.api.liveSessions(this.liveCourse)); } catch { this.msg.set('common.error'); }
  }

  async createLive(): Promise<void> {
    if (!this.lvTitle.trim() || !this.lvUrl.trim() || !this.lvStart) { this.msg.set('cm.errLive'); return; }
    if (!/^https:\/\//i.test(this.lvUrl.trim())) { this.msg.set('cm.errLiveUrl'); return; }
    const startsAtUtc = new Date(this.lvStart).toISOString();
    const endsAtUtc = this.lvEnd ? new Date(this.lvEnd).toISOString() : null;
    if (endsAtUtc && endsAtUtc <= startsAtUtc) { this.msg.set('cm.errLiveEnd'); return; }
    try {
      await this.api.createLiveSession(this.liveCourse, { title: this.lvTitle.trim(), provider: this.lvProvider, joinUrl: this.lvUrl.trim(), startsAtUtc, endsAtUtc });
      this.lvTitle = ''; this.lvUrl = ''; this.lvStart = ''; this.lvEnd = ''; this.msg.set('cm.liveCreated');
      this.live.set(await this.api.liveSessions(this.liveCourse));
    } catch { /* toast */ }
  }

  async joinLive(s: LiveSessionRow): Promise<void> {
    try {
      const res = await this.api.joinLive(s.id);
      if (res.joinUrl) this.pendingJoin.set({ id: s.id, url: res.joinUrl });
    } catch { /* toast */ }
  }

  async toggleAttendance(s: LiveSessionRow): Promise<void> {
    if (this.attendance()?.id === s.id) { this.attendance.set(null); return; }
    try { this.attendance.set({ id: s.id, rows: await this.api.liveAttendance(s.id) }); } catch { /* toast */ }
  }

  async pickCourse(): Promise<void> {
    this.threads.set([]); this.openThread.set(null); this.msg.set('');
    if (!this.course) return;
    try { this.threads.set(await this.api.threads(this.course)); } catch { this.msg.set('common.error'); }
  }

  async open(t: ThreadRow): Promise<void> {
    if (this.openThread()?.id === t.id) { this.openThread.set(null); return; }
    this.openThread.set(null); this.threadFailed.set(false); this.replyBody = '';
    try { this.openThread.set(await this.api.thread(t.id)); } catch { this.threadFailed.set(true); }
  }

  async createThread(): Promise<void> {
    if (!this.newTitle.trim() || !this.newBody.trim()) { this.msg.set('cm.errThread'); return; }
    try {
      await this.api.createThread(this.course, { title: this.newTitle.trim(), body: this.newBody.trim(), lessonId: null });
      this.newTitle = ''; this.newBody = ''; this.msg.set('cm.threaded');
      this.threads.set(await this.api.threads(this.course));
    } catch { /* toast by interceptor */ }
  }

  async reply(): Promise<void> {
    const t = this.openThread(); if (!t || !this.replyBody.trim()) return;
    try {
      await this.api.reply(t.id, { body: this.replyBody.trim(), parentPostId: null });
      this.replyBody = ''; this.openThread.set(await this.api.thread(t.id));
    } catch { /* toast by interceptor */ }
  }

  async upvoteThread(): Promise<void> {
    const t = this.openThread(); if (!t) return;
    try { await this.api.upvoteThread(t.id); this.openThread.set(await this.api.thread(t.id)); } catch { /* toast */ }
  }

  async upvotePost(id: string): Promise<void> {
    const t = this.openThread(); if (!t) return;
    try { await this.api.upvotePost(id); this.openThread.set(await this.api.thread(t.id)); } catch { /* toast */ }
  }

  async pin(): Promise<void> {
    const t = this.openThread(); if (!t) return;
    try { await this.api.pinThread(t.id); this.openThread.set(await this.api.thread(t.id)); this.threads.set(await this.api.threads(this.course)); } catch { /* toast */ }
  }

  async announce(): Promise<void> {
    if (!this.annTitle.trim() || !this.annBody.trim()) { this.msg.set('cm.errAnn'); return; }
    try {
      await this.api.announce({ courseId: this.annCourse || null, title: this.annTitle.trim(), body: this.annBody.trim() });
      this.annTitle = ''; this.annBody = ''; this.msg.set('cm.announced');
      this.announcements.set(await this.api.announcements());
    } catch { /* toast by interceptor */ }
  }
}
