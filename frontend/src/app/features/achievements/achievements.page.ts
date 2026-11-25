import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { AuthService } from '../../core/services/auth.service';
import { EngagementService, CertificateRow, CertificateVerify, GamificationMe, LeaderboardRow, LinkCode } from '../../core/services/engagement.service';
import { AdminService } from '../../core/services/admin.service';
import { LearningService } from '../../core/services/learning.service';
import { User } from '../../core/models/models';
import { MyCourse } from '../../core/models/learning';

/** Achievements: XP/streak/badges, leaderboard, certificates (Student), certificate verification (all), parent link code (Student). */
@Component({ selector: 'app-achievements', imports: [DatePipe, FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './achievements.page.html', styleUrl: './achievements.page.scss' })
export class AchievementsPage implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(EngagementService);
  private readonly admin = inject(AdminService);
  private readonly learning = inject(LearningService);
  readonly me = signal<GamificationMe | null>(null);
  readonly board = signal<LeaderboardRow[] | null>(null);
  readonly certs = signal<CertificateRow[] | null>(null);
  readonly verify = signal<CertificateVerify | null>(null);
  readonly verifyFailed = signal(false);
  readonly linkCode = signal<LinkCode | null>(null);
  // manager certificate issuing: search for the enrolled student, never paste raw ids
  readonly issueCourses = signal<MyCourse[] | null>(null);
  readonly studentHits = signal<User[] | null>(null);
  readonly pickedStudent = signal<User | null>(null);
  readonly issued = signal<{ id: string; verificationCode: string } | null>(null);
  readonly issueErr = signal('');
  verifyCode = '';
  issueCourseId = '';
  studentQuery = '';

  ngOnInit(): void {
    if (this.auth.hasRole('Student')) {
      this.api.gamificationMe().then((v) => this.me.set(v)).catch(() => this.me.set(null));
      this.api.myCertificates().then((v) => this.certs.set(v)).catch(() => this.certs.set([]));
    }
    this.api.leaderboard().then((v) => this.board.set(v)).catch(() => this.board.set([]));
    if ((this.auth.hasRole('TenantAdmin') || this.auth.hasRole('Instructor'))) {
      this.learning.myCourses().then((v) => this.issueCourses.set(v)).catch(() => this.issueCourses.set([]));
    }
  }

  async findStudents(): Promise<void> {
    this.issueErr.set(''); this.pickedStudent.set(null); this.issued.set(null);
    try { const r = await this.admin.users(this.studentQuery, 'Student'); this.studentHits.set(r.items); }
    catch { this.studentHits.set([]); this.issueErr.set('common.error'); }
  }

  pickStudent(u: User): void { this.pickedStudent.set(u); this.studentHits.set(null); }

  async issue(): Promise<void> {
    const st = this.pickedStudent();
    if (!st || !this.issueCourseId) { this.issueErr.set('ach.errIssue'); return; }
    this.issueErr.set('');
    try { this.issued.set(await this.api.issueCertificate(st.id, this.issueCourseId)); }
    catch { this.issueErr.set('ach.errIssueFailed'); }
  }

  async checkVerify(): Promise<void> {
    const code = this.verifyCode.trim(); if (!code) return;
    this.verify.set(null); this.verifyFailed.set(false);
    try { this.verify.set(await this.api.verifyCertificate(code)); } catch { this.verifyFailed.set(true); }
  }

  async makeLinkCode(): Promise<void> {
    try { this.linkCode.set(await this.api.createLinkCode()); } catch { /* toast */ }
  }

  readonly downloading = signal<string | null>(null);
  async downloadCert(c: { id: string; courseTitle: string }): Promise<void> {
    this.downloading.set(c.id);
    try {
      const blob = await this.api.certificatePdf(c.id);
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `certificate-${c.courseTitle.replace(/\s+/g, '-').toLowerCase()}.pdf`;
      a.click();
      URL.revokeObjectURL(a.href);
    } catch { /* toast by interceptor */ } finally { this.downloading.set(null); }
  }
}
