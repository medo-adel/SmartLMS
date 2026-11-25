import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/services/admin.service';
import { EngagementService } from '../../core/services/engagement.service';
import { LearningService } from '../../core/services/learning.service';
import { CourseDetail, MyCourse } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** TenantAdmin: courses submitted by instructors, waiting for approval. Approve publishes; reject returns the course with a comment. */
@Component({ selector: 'app-approvals', imports: [FormsModule, RouterLink, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './approvals.page.html', styleUrl: './approvals.page.scss' })
export class ApprovalsPage implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly api = inject(LearningService);
  readonly courses = signal<MyCourse[] | null>(null);
  readonly failed = signal(false);
  readonly rejecting = signal('');
  readonly info = signal('');
  tab = 'PendingApproval';
  comment = '';
  readonly previewId = signal('');
  readonly preview = signal<CourseDetail | null>(null);
  readonly previewFailed = signal(false);
  readonly tabs = ['PendingApproval', 'Published', 'Draft', 'Rejected', 'All'];

  readonly shown = computed(() => {
    const all = this.courses() ?? [];
    return this.tab === 'All' ? all : all.filter((c) => c.status === this.tab);
  });
  readonly pendingCount = computed(() => (this.courses() ?? []).filter((c) => c.status === 'PendingApproval').length);

  private readonly engage = inject(EngagementService);
  readonly pendingLinks = signal<{ linkId: string; parentName: string; studentName: string; createdAtUtc: string; status: string }[] | null>(null);
  readonly linksUnavailable = signal(false);

  ngOnInit(): void { void this.load(); void this.loadLinks(); }

  /** Pending parent-link requests. Uses the added GET /parent/links/pending; on older backends the section hides itself. */
  private async loadLinks(): Promise<void> {
    try { this.pendingLinks.set(await this.engage.pendingLinks()); }
    catch (e) {
      this.pendingLinks.set(null);
      // 404 = older backend without the route (show an update-required notice); anything else = a real failure.
      this.linksUnavailable.set((e as { status?: number })?.status === 404);
      if ((e as { status?: number })?.status !== 404) this.info.set('common.error');
    }
  }

  async setLink(linkId: string, approve: boolean): Promise<void> {
    try { await this.engage.setLink(linkId, approve); this.pendingLinks.set((this.pendingLinks() ?? []).filter((l) => l.linkId !== linkId)); } catch { /* toast */ }
  }
  async load(): Promise<void> {
    this.failed.set(false);
    try { this.courses.set(await this.api.myCourses()); } catch { this.failed.set(true); }
  }
  async setTab(t: string): Promise<void> { this.tab = t; }

  /** Lets the admin inspect the course outline (sections and lesson titles) before approving or rejecting. */
  async togglePreview(id: string): Promise<void> {
    if (this.previewId() === id) { this.previewId.set(''); return; }
    this.previewId.set(id); this.preview.set(null); this.previewFailed.set(false);
    try { this.preview.set(await this.api.course(id)); } catch { this.previewFailed.set(true); }
  }

  async approve(id: string): Promise<void> {
    this.info.set('');
    try { await this.admin.approveCourse(id); this.info.set('approved'); await this.load(); } catch { /* toast by interceptor */ }
  }
  async reject(id: string): Promise<void> {
    if (!this.comment.trim()) return;
    this.info.set('');
    try { await this.admin.rejectCourse(id, this.comment.trim()); this.rejecting.set(''); this.comment = ''; this.info.set('rejected'); await this.load(); } catch { /* toast by interceptor */ }
  }
}
