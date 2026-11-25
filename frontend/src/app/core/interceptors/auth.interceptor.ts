import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { from, switchMap, catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';
import { DeviceService } from '../services/device.service';
import { TenantService } from '../services/tenant.service';

const isApi = (url: string) => url.startsWith(environment.apiBaseUrl);
const isAuthCall = (url: string) => /\/auth\/(login|refresh|register|forgot-password|reset-password)$/.test(url);

/** Set on requests that must go out without X-Tenant-Id even when an academy is selected (platform login retry). */
export const SKIP_TENANT = new HttpContextToken<boolean>(() => false);

/** Adds X-Tenant-Id, X-Device-Id and the JWT; on a 401 refreshes once (single-flight) and retries. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApi(req.url)) return next(req);
  const auth = inject(AuthService), device = inject(DeviceService), tenant = inject(TenantService), router = inject(Router);
  const decorate = (r: HttpRequest<unknown>) => {
    let headers = r.headers;
    // No tenant key = platform context (SuperAdmin). The API treats a missing header as platform login.
    const tk = tenant.tenantKey();
    if (tk && !r.context.get(SKIP_TENANT)) headers = headers.set('X-Tenant-Id', tk);
    if (device.fingerprintSync) headers = headers.set('X-Device-Id', device.fingerprintSync);
    const token = auth.accessToken;
    if (token) headers = headers.set('Authorization', `Bearer ${token}`);
    return r.clone({ headers });
  };
  return next(decorate(req)).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !isAuthCall(req.url) && auth.accessToken) {
        return from(auth.refresh()).pipe(switchMap((ok) => {
          if (!ok) { void router.navigateByUrl('/login'); return throwError(() => err); }
          return next(decorate(req));
        }));
      }
      return throwError(() => err);
    }),
  );
};
