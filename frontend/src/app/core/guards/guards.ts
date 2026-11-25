import { inject } from '@angular/core';
import { CanActivateFn, Router, ActivatedRouteSnapshot } from '@angular/router';
import { Capacitor } from '@capacitor/core';
import { AuthService } from '../services/auth.service';
import { BiometricService } from '../services/biometric.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService), router = inject(Router), bio = inject(BiometricService);
  if (!auth.isAuthenticated()) return router.parseUrl('/login');
  if (Capacitor.isNativePlatform() && bio.enabled() && !auth.unlocked()) return router.parseUrl('/lock');
  return true;
};
/** Keeps signed-in users off the login page. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService), router = inject(Router);
  return auth.isAuthenticated() ? router.parseUrl('/app/dashboard') : true;
};
/** Route data: { roles: ['TenantAdmin', ...] }. UI hiding is a convenience; the API enforces roles. */
export const roleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const auth = inject(AuthService), router = inject(Router);
  const roles = (route.data['roles'] as string[] | undefined) ?? [];
  if (!auth.isAuthenticated()) return router.parseUrl('/login');
  return roles.length === 0 || auth.hasRole(...roles) ? true : router.parseUrl('/app/dashboard');
};
