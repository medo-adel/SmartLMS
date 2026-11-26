import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideAppInitializer, inject } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, RouteReuseStrategy } from '@angular/router';
import { IonicRouteStrategy, provideIonicAngular } from '@ionic/angular';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { DeviceService } from './core/services/device.service';
import { I18nService } from './core/services/i18n.service';
import { TenantService } from './core/services/tenant.service';
import { AuthService } from './core/services/auth.service';
import { BiometricService } from './core/services/biometric.service';
import { PushService } from './core/services/push.service';

/** Startup order matters: device id and language first, then session, then branding (needs X-Tenant-Id / X-Device-Id). */
async function bootstrap(): Promise<void> {
  const device = inject(DeviceService), i18n = inject(I18nService), tenant = inject(TenantService), auth = inject(AuthService), bio = inject(BiometricService);
  await device.init();
  await i18n.init(null);
  await auth.restore();
  await bio.init();
  await tenant.init();
  // Instantiating the service arms an effect: it registers the device whenever a session exists.
  inject(PushService);
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: RouteReuseStrategy, useClass: IonicRouteStrategy },
    provideIonicAngular(),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, errorInterceptor])),
    provideAppInitializer(bootstrap),
  ],
};
