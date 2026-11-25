import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { I18nService } from '../services/i18n.service';
import { ToastService } from '../services/toast.service';

/** Set on background calls (progress heartbeats) that retry on their own, so a failure does not toast every few seconds. */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

/** Maps an HTTP failure to a readable message (server message when the API sent one). */
export function messageFor(err: HttpErrorResponse, t: (k: string) => string): string {
  if (err.status === 0) return t('err.network');
  if (err.status >= 500) return t('common.error'); // never show raw server text for server faults
  if (err.status === 403) return err.error?.message ?? t('err.forbidden');
  if (err.status === 401) return t('err.session');
  return err.error?.message ?? t('common.error');
}

/** Global error toast. Silent for login (the form shows its own message) and for 401, which the auth interceptor handles. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService), i18n = inject(I18nService);
  return next(req).pipe(catchError((err: unknown) => {
    if (err instanceof HttpErrorResponse && req.url.startsWith(environment.apiBaseUrl) && !/\/auth\/login$/.test(req.url) && err.status !== 401 && !req.context.get(SILENT_ERRORS) && !/\/tenants\/current\//.test(req.url)) {
      void toast.error(messageFor(err, (k) => i18n.t(k)));
    }
    return throwError(() => err);
  }));
};
