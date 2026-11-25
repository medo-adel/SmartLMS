import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';

export interface Shares { defaultInstructorSharePercent: number | null; overrides: { courseId: string; instructorId: string; sharePercent: number }[]; }
export interface Statement { fromUtc: string; toUtc: string; instructors: { instructorId: string; name: string; total: number; courseSales: number; bundleAndPath: number; subscription: number; subscriptionQualifiedMinutes: number; subscriptionLessonsCounted: number; previousPeriodSubscription: number; flags: string[] }[]; unallocatedSubscriptionRevenue: number; heldNoRecordedShare: number; note: string; }
export type HeldItem = { orderItemId: string; courseId: string; instructorId: string; amount: number; kind: string };
export interface Held { fromUtc: string; toUtc: string; items: { orderItemId: string; courseId: string; instructorId: string; amount: number; kind: string }[]; totalHeld: number; }
export interface Debits { items: { id: string; instructorId: string; orderRefundId: string | null; sourcePayoutId: string | null; amount: number; remainingAmount: number; currency: string; createdAtUtc: string }[]; outstanding: number; }
export interface PayoutRow { id: string; instructorId: string; name: string | null; periodStartUtc: string; periodEndUtc: string; amount: number; currency: string; status: string; paidAtUtc: string | null; paymentReference: string | null; }
export interface Payouts { items: PayoutRow[]; page: number; pageSize: number; totalCount: number; }
export interface MyRevenue { fromUtc: string; toUtc: string; courses: { courseId: string; title: string; sales: number; net: number; earned: number; sharePercent: number }[]; totalEarned: number; payouts: PayoutRow[]; }
export interface AdminDashboard { kpis: { users: number; publishedCourses: number; activeEnrollments: number; revenueLast30Days: number; revenueAllTime: number; paidOrdersLast30Days: number }; queues: { coursesAwaitingApproval: number; manualPaymentsToReview: number; payoutsToApprove: number; failedWebhookDeliveries: number }; revenueByDay: { day: string; revenue: number; orders: number }[]; }
export interface AuditRow { id: string; userId: string | null; userName: string | null; action: string; entityName: string; entityId: string; oldValuesJson: string | null; newValuesJson: string | null; ipAddress: string | null; correlationId: string | null; occurredAtUtc: string; }
export interface AuditPage { items: AuditRow[]; page: number; pageSize: number; totalCount: number; }
export interface BinView { items: { entity: string; id: string; name: string; deletedAtUtc: string | null; deletedBy: string | null }[]; supportedEntities: string[]; }
export interface ApiKeyRow { id: string; name: string; prefix: string; scopesJson: string; expiresAtUtc: string | null; lastUsedAtUtc: string | null; revokedAtUtc: string | null; createdAtUtc: string; }
export interface ApiKeyCreated { id: string; name: string; prefix: string; scopes: string[]; expiresAtUtc: string | null; key: string; note: string; }
export interface WebhookRow { id: string; url: string; eventTypes: string[]; isActive: boolean; consecutiveFailures: number; createdAtUtc: string; }
export interface WebhookCreated { id: string; url: string; eventTypes: string[]; isActive: boolean; secret: string; note: string; }
export interface WebhookDelivery { id: string; eventType: string; status: string; attemptCount: number; lastHttpStatus: number | null; lastError: string | null; nextAttemptAtUtc: string | null; deliveredAtUtc: string | null; createdAtUtc: string; }
export interface WebhookDeliveries { items: WebhookDelivery[]; page: number; pageSize: number; totalCount: number; }

/** Finance + ops admin endpoints (CommerceController). TenantAdmin unless noted. */
@Injectable({ providedIn: 'root' })
export class FinanceService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;
  private async unwrap<T>(req: Promise<ApiResponse<T>>): Promise<T> { const res = await req; if (!res.success) throw new Error(res.message ?? 'request failed'); return res.data as T; }
  private get<T>(path: string, params?: HttpParams) { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<T>>(`${this.base}${path}`, { params }))); }
  private post<T>(path: string, body: unknown = {}) { return this.unwrap(firstValueFrom(this.http.post<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private put<T>(path: string, body: unknown) { return this.unwrap(firstValueFrom(this.http.put<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private del(path: string) { return this.unwrap(firstValueFrom(this.http.delete<ApiResponse<unknown>>(`${this.base}${path}`))); }

  dashboard() { return this.get<AdminDashboard>('/dashboards/admin'); }
  shares() { return this.get<Shares>('/instructor-revenue/shares'); }
  setDefaultShare(percent: number | null) { return this.put('/instructor-revenue/default', { percent }); }
  statement(fromUtc: string, toUtc: string) { return this.get<Statement>('/instructor-payouts/statement', new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc)); }
  held(fromUtc: string, toUtc: string) { return this.get<Held>('/instructor-revenue/held', new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc)); }
  releaseHeld(r: { orderItemId: string; courseId: string; instructorId: string; percent: number }) { return this.put('/instructor-revenue/held', r); }
  debits() { return this.get<Debits>('/instructor-debits'); }
  payouts(page = 1, size = 20) { return this.get<Payouts>('/instructor-payouts', new HttpParams().set('page', page).set('size', size)); }
  calculatePayouts(periodStartUtc: string, periodEndUtc: string) { return this.post<{ jobId: string; status: string; queued: boolean; result: string | null }>('/instructor-payouts/calculate', { periodStartUtc, periodEndUtc }); }
  updatePayout(id: string, status: string, paymentReference: string | null) { return this.put(`/instructor-payouts/${id}`, { status, paymentReference }); }
  job(id: string) { return this.get<{ id: string; type: string; status: string; progressPercent: number; errorMessage: string | null; startedAtUtc: string | null; completedAtUtc: string | null }>(`/jobs/${encodeURIComponent(id)}`); }
  recordRefund(orderId: string, amount: number, reference: string, reason: string | null) { return this.post(`/orders/${orderId}/refunds`, { amount, reference, reason }); }
  setCourseShare(courseId: string, instructorId: string, percent: number) { return this.put(`/courses/${courseId}/instructor-share`, { instructorId, percent }); }
  subscriptionControls() { return this.get<{ minWatchPercent: number | null; minAverageLessonSeconds: number | null; jumpAlertPercent: number | null }>('/instructor-revenue/subscription-controls'); }
  setSubscriptionControls(r: { minWatchPercent: number | null; minAverageLessonSeconds: number | null; jumpAlertPercent: number | null }) { return this.put<{ minWatchPercent: number | null; minAverageLessonSeconds: number | null; jumpAlertPercent: number | null }>('/instructor-revenue/subscription-controls', r); }
  /** Current learning weight (added backend route); older backends 404 and the form says an update is required. */
  learningWeight(courseId: string) { return this.get<{ courseId: string; nonVideoCourseSeconds: number | null }>(`/courses/${courseId}/learning-weight`); }
  setLearningWeight(courseId: string, nonVideoCourseSeconds: number) { return this.put(`/courses/${courseId}/learning-weight`, { nonVideoCourseSeconds }); }
  webhooks() { return this.get<WebhookRow[]>('/webhooks'); }
  createWebhook(r: { url: string; eventTypes: string[]; isActive: boolean | null }) { return this.post<WebhookCreated>('/webhooks', r); }
  updateWebhook(id: string, r: { url: string; eventTypes: string[]; isActive: boolean | null }) { return this.put(`/webhooks/${id}`, r); }
  deleteWebhook(id: string) { return this.del(`/webhooks/${id}`); }
  webhookDeliveries(id: string) { return this.get<WebhookDeliveries>(`/webhooks/${id}/deliveries`); }
  retryDelivery(id: string) { return this.post(`/webhooks/deliveries/${id}/retry`); }
  myRevenue(from?: string, to?: string) { let p = new HttpParams(); if (from) p = p.set('from', from); if (to) p = p.set('to', to); return this.get<MyRevenue>('/instructor/revenue', p); }

  auditLogs(f: { entity?: string; action?: string; from?: string; to?: string; page?: number; size?: number }) {
    let p = new HttpParams().set('page', f.page ?? 1).set('size', f.size ?? 20);
    if (f.entity) p = p.set('entity', f.entity); if (f.action) p = p.set('action', f.action);
    if (f.from) p = p.set('from', f.from); if (f.to) p = p.set('to', f.to);
    return this.get<AuditPage>('/audit-logs', p);
  }
  recycleBin(entity?: string) { let p = new HttpParams(); if (entity) p = p.set('entity', entity); return this.get<BinView>('/recycle-bin', p); }
  restore(entity: string, id: string) { return this.post(`/recycle-bin/${encodeURIComponent(entity)}/${encodeURIComponent(id)}/restore`); }
  apiKeys() { return this.get<ApiKeyRow[]>('/api-keys'); }
  createApiKey(name: string, scopes: string[], expiresAtUtc: string | null) { return this.post<ApiKeyCreated>('/api-keys', { name, scopes, expiresAtUtc }); }
  revokeApiKey(id: string) { return this.del(`/api-keys/${id}`); }
}
