import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { AdminDashboard, AppNotification, InstructorDashboard, ParentChild, ParentSummary, StudentDashboard, User, UserDevice } from '../models/models';

/** All calls to the .NET API (the app never talks to the AI service). Unwraps the ApiResponse envelope. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  private async get<T>(path: string, params?: HttpParams): Promise<T> {
    const res = await firstValueFrom(this.http.get<ApiResponse<T>>(`${this.base}${path}`, { params }));
    if (!res.success) throw new Error(res.message ?? 'request failed');
    return res.data as T;
  }
  studentDashboard() { return this.get<StudentDashboard>('/dashboards/student'); }
  instructorDashboard() { return this.get<InstructorDashboard>('/dashboards/instructor'); }
  adminDashboard() { return this.get<AdminDashboard>('/dashboards/admin'); }
  parentChildren() { return this.get<ParentChild[]>('/parent/children'); }
  parentSummary(id: string) { return this.get<ParentSummary>(`/parent/children/${encodeURIComponent(id)}/summary`); }
  me() { return this.get<User>('/me'); }
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<ApiResponse<unknown>>(`${this.base}/me/change-password`, { currentPassword, newPassword }));
    if (!res.success) throw new Error(res.message ?? 'request failed');
  }
  devices() { return this.get<UserDevice[]>('/me/devices'); }
  async removeDevice(id: string) { await firstValueFrom(this.http.delete(`${this.base}/me/devices/${encodeURIComponent(id)}`)); }
  notifications() { return this.get<AppNotification[]>('/notifications'); }
  async markRead(id: string) { await firstValueFrom(this.http.post(`${this.base}/notifications/${encodeURIComponent(id)}/read`, {})); }
  /** Report export as a file blob. kind: orders | enrollments | revenue (instructors: enrollments, revenue). */
  exportReport(kind: string, format: 'csv' | 'xlsx') {
    const params = new HttpParams().set('format', format);
    return firstValueFrom(this.http.get(`${this.base}/reports/${encodeURIComponent(kind)}/export`, { params, responseType: 'blob' }));
  }
}
