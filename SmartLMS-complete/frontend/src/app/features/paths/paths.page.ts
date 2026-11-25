import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { LearningService } from '../../core/services/learning.service';
import { AuthService } from '../../core/services/auth.service';
import { MyCourse, PathDetail } from '../../core/models/learning';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Learning paths: list + enroll for everyone (list is published-only); create/edit for TenantAdmin/Instructor, delete for TenantAdmin. */
@Component({ selector: 'app-paths', imports: [RouterLink, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './paths.page.html', styleUrl: './paths.page.scss' })
export class PathsPage implements OnInit {
  private readonly api = inject(LearningService);
  readonly auth = inject(AuthService);
  readonly paths = signal<PathDetail[] | null>(null);
  readonly failed = signal(false);
  readonly done = signal<string | null>(null);
  readonly msg = signal('');
  readonly confirmDel = signal<string | null>(null);
  readonly manageAvailable = signal(true);
  readonly myCourses = signal<MyCourse[]>([]);
  editing: string | null = null;
  pfTitle = ''; pfDesc = ''; pfStatus = 'Draft'; pfCert = false; pfCourses = new Set<string>();

  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const staff = this.auth.hasRole('TenantAdmin', 'Instructor');
      let list: { id: string }[];
      if (staff) {
        try { list = await this.api.managePaths(); this.manageAvailable.set(true); }
        catch (e) {
          if ((e as { status?: number })?.status === 404) {
            // Older backend without the manage route: fall back to the published-only list and say so.
            list = await this.api.paths(); this.manageAvailable.set(false);
          } else throw e; // 401/403/5xx/network: surface, never silently fall back
        }
      } else list = await this.api.paths();
      this.paths.set(await Promise.all(list.map((p) => this.api.path(p.id))));
      if (staff) this.myCourses.set(await this.api.myCourses());
    } catch { this.failed.set(true); }
  }
  async enroll(p: PathDetail): Promise<void> {
    try { await this.api.enrollPath(p.id); this.done.set(p.id); } catch { /* toast */ }
  }

  startEdit(p: PathDetail): void {
    this.editing = p.id; this.pfTitle = p.title; this.pfDesc = p.description ?? '';
    this.pfStatus = p.status; this.pfCert = p.issuesCertificate; this.pfCourses = new Set(p.courses.map((c) => c.id));
  }
  resetForm(): void { this.editing = null; this.pfTitle = ''; this.pfDesc = ''; this.pfStatus = 'Draft'; this.pfCert = false; this.pfCourses = new Set(); }
  toggleCourse(id: string): void { this.pfCourses.has(id) ? this.pfCourses.delete(id) : this.pfCourses.add(id); }

  async save(): Promise<void> {
    if (!this.pfTitle.trim()) { this.msg.set('cm.errPath'); return; }
    const body = { title: this.pfTitle.trim(), description: this.pfDesc.trim() || null, status: this.pfStatus, issuesCertificate: this.pfCert, courseIds: [...this.pfCourses] };
    try {
      if (this.editing) await this.api.updatePath(this.editing, body);
      else await this.api.createPath(body);
      this.msg.set('cm.pathSaved'); this.resetForm(); await this.load();
    } catch { /* toast by interceptor */ }
  }

  async remove(p: PathDetail): Promise<void> {
    if (this.confirmDel() !== p.id) { this.confirmDel.set(p.id); return; }
    try { await this.api.deletePath(p.id); this.confirmDel.set(null); this.msg.set('cm.pathDeleted'); await this.load(); } catch { /* toast */ }
  }
}
