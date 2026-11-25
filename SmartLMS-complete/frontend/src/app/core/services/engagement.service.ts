import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { SILENT_ERRORS } from '../interceptors/error.interceptor';

export interface GamificationMe { xp: number; level: number; currentStreakDays: number; longestStreakDays: number; badges: { code: string; name: string; awardedAtUtc: string }[]; }
export interface LeaderboardRow { rank: number; name: string; xp: number; isMe: boolean; }
export interface CertificateRow { id: string; courseId: string; courseTitle: string; issuedAtUtc: string; verificationCode: string; }
export interface CertificateVerify { valid: boolean; holderName: string; course: string; issuedAtUtc: string; status: string; }
export interface LinkCode { code: string; expiresAtUtc: string; }
export interface ParentChild { studentId: string; fullName: string; }
export interface ChildSummary { studentId: string; courses: { title: string; status: string; progressPercent: number }[]; averageExamScorePercent: number | null; weeklyStudyHours: number; attendancePercent: number | null; riskAlerts: string[]; }
export interface ParentAlert { id: string; studentUserId: string; alertType: string; message: string; createdAtUtc: string; readAtUtc: string | null; }

/** Certificates, XP/leaderboard, parent links (EngagementController). */
@Injectable({ providedIn: 'root' })
export class EngagementService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;
  private async unwrap<T>(req: Promise<ApiResponse<T>>): Promise<T> { const res = await req; if (!res.success) throw new Error(res.message ?? 'request failed'); return res.data as T; }
  private get<T>(path: string, params?: HttpParams) { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<T>>(`${this.base}${path}`, { params }))); }
  private post<T>(path: string, body: unknown = {}) { return this.unwrap(firstValueFrom(this.http.post<ApiResponse<T>>(`${this.base}${path}`, body))); }

  gamificationMe() { return this.get<GamificationMe>('/gamification/me'); }
  leaderboard(courseId?: string) { let p = new HttpParams(); if (courseId) p = p.set('courseId', courseId); return this.get<LeaderboardRow[]>('/gamification/leaderboard', p); }
  myCertificates() { return this.get<CertificateRow[]>('/certificates/mine'); }
  issueCertificate(userId: string, courseId: string) { return this.post<{ id: string; verificationCode: string }>('/certificates/issue', { userId, courseId }); }
  verifyCertificate(code: string) { return this.get<CertificateVerify>(`/certificates/verify/${encodeURIComponent(code)}`); }
  /** Authenticated fetch: the endpoint requires a bearer token, so a plain anchor href would 401. Downloads as blob. */
  async certificatePdf(id: string): Promise<Blob> { return firstValueFrom(this.http.get(`${this.base}/certificates/${encodeURIComponent(id)}/pdf`, { responseType: 'blob' })); }

  createLinkCode() { return this.post<LinkCode>('/students/me/link-codes'); }
  requestLink(code: string) { return this.post<{ linkId: string; status: string }>('/parent/links', { code }); }
  pendingLinks() { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<{ linkId: string; parentName: string; studentName: string; createdAtUtc: string; status: string }[]>>(`${this.base}/parent/links/pending`, { context: new HttpContext().set(SILENT_ERRORS, true) }))); }
  setLink(linkId: string, approve: boolean) { return this.unwrap(firstValueFrom(this.http.put<ApiResponse<unknown>>(`${this.base}/parent/links/${encodeURIComponent(linkId)}`, { approve }))); }
  children() { return this.get<ParentChild[]>('/parent/children'); }
  childSummary(studentId: string) { return this.get<ChildSummary>(`/parent/children/${encodeURIComponent(studentId)}/summary`); }
  alerts() { return this.get<ParentAlert[]>('/parent/alerts'); }
}
