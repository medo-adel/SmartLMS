import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { CategoryItem, FeatureFlag, SupportAccess, Tenant, User, UserDevice } from '../models/models';
import { Paged } from '../models/learning';

/** Administration endpoints: users (TenantAdmin), tenants (SuperAdmin), academy settings, categories, course approvals. The API enforces every role. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  private async unwrap<T>(req: Promise<ApiResponse<T>>): Promise<T> {
    const res = await req;
    if (!res.success) throw new Error(res.message ?? 'request failed');
    return res.data as T;
  }
  private get<T>(path: string, params?: HttpParams) { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<T>>(`${this.base}${path}`, { params }))); }
  private post<T>(path: string, body: unknown = {}) { return this.unwrap(firstValueFrom(this.http.post<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private put<T>(path: string, body: unknown) { return this.unwrap(firstValueFrom(this.http.put<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private del<T>(path: string) { return this.unwrap(firstValueFrom(this.http.delete<ApiResponse<T>>(`${this.base}${path}`))); }
  private id(v: string) { return encodeURIComponent(v); }

  // users (list: SuperAdmin/TenantAdmin/Instructor; manage: TenantAdmin)
  users(search: string, role: string, page = 1) {
    let p = new HttpParams().set('page', page).set('pageSize', 20);
    if (search.trim()) p = p.set('search', search.trim());
    if (role) p = p.set('role', role);
    return this.get<Paged<User>>('/users', p);
  }
  user(id: string) { return this.get<User>(`/users/${this.id(id)}`); }
  createUser(u: { fullName: string; email: string; password: string; roles: string[]; phoneNumber: string | null; preferredLanguage: string | null }) { return this.post<User>('/users', u); }
  updateUser(id: string, u: { fullName: string; phoneNumber: string | null; preferredLanguage: string }) { return this.put<User>(`/users/${this.id(id)}`, u); }
  setUserRoles(id: string, roles: string[]) { return this.put<User>(`/users/${this.id(id)}/roles`, { roles }); }
  deactivateUser(id: string) { return this.post<unknown>(`/users/${this.id(id)}/deactivate`); }
  userDevices(id: string) { return this.get<UserDevice[]>(`/users/${this.id(id)}/devices`); }
  resetUserDevices(id: string, reason: string | null) { return this.post<number>(`/users/${this.id(id)}/devices/reset`, { reason }); }

  // course approvals (TenantAdmin)
  approveCourse(id: string) { return this.post<unknown>(`/courses/${this.id(id)}/approve`); }
  rejectCourse(id: string, comment: string) { return this.post<unknown>(`/courses/${this.id(id)}/reject`, { comment }); }

  // tenants (SuperAdmin)
  tenants(page = 1) { return this.get<Paged<Tenant>>('/tenants', new HttpParams().set('page', page).set('pageSize', 20)); }
  createTenant(t: { name: string; slug: string; subdomain: string | null; adminFullName: string; adminEmail: string; adminPassword: string; maxMobileDevices: number; maxWebDevices: number; defaultLanguage: string; timeZoneId: string; currency: string }) { return this.post<Tenant>('/tenants', t); }
  updateTenant(id: string, t: { name: string; subdomain: string | null; isActive: boolean; maxMobileDevices: number; maxWebDevices: number; defaultLanguage: string; timeZoneId: string; currency: string }) { return this.put<Tenant>(`/tenants/${this.id(id)}`, t); }
  grantSupport(id: string, minutes: number | null) { return this.post<SupportAccess>(`/tenants/${this.id(id)}/support-access`, { minutes }); }
  revokeSupport(id: string) { return this.del<unknown>(`/tenants/${this.id(id)}/support-access`); }

  // academy settings (TenantAdmin)
  updateSettings(s: { name: string; primaryColor: string | null; secondaryColor: string | null; accentColor: string | null; defaultLanguage: string; timeZoneId: string; currency: string }) { return this.put<Tenant>('/tenant/settings', s); }
  uploadLogo(file: File) { const f = new FormData(); f.append('file', file, file.name); return this.post<Tenant>('/tenant/logo', f); }
  featureFlags() { return this.get<FeatureFlag[]>('/tenant/feature-flags'); }
  setFeatureFlag(key: string, isEnabled: boolean) { return this.put<FeatureFlag>(`/tenant/feature-flags/${encodeURIComponent(key)}`, { isEnabled }); }

  // categories (list is shared; writes are TenantAdmin)
  categories() { return this.get<CategoryItem[]>('/categories'); }
  createCategory(c: { name: string; parentCategoryId: string | null; sortOrder: number; iconName: string | null }) { return this.post<unknown>('/categories', c); }
  updateCategory(id: string, c: { name: string; parentCategoryId: string | null; sortOrder: number; iconName: string | null }) { return this.put<unknown>(`/categories/${this.id(id)}`, c); }
  deleteCategory(id: string) { return this.del<unknown>(`/categories/${this.id(id)}`); }
}
