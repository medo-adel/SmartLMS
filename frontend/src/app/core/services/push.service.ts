import { Injectable, effect, inject } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { PushNotifications } from '@capacitor/push-notifications';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { ToastController } from '@ionic/angular';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { AuthService } from './auth.service';

/**
 * Native push notifications (FCM via @capacitor/push-notifications). Registers the device token with the
 * API (POST /notifications/push-token, which dedupes and prunes dead tokens), shows an in-app toast when a
 * notification arrives in the foreground, and opens the notifications page when one is tapped.
 * No-op on web: browsers are served by the in-app notifications page; web push would need an FCM web setup
 * (VAPID + service worker) that is not part of this build.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastController);
  private readonly auth = inject(AuthService);
  private started = false;

  constructor() {
    // Registers once per app run, as soon as a session exists (cold start or a later login).
    effect(() => { if (this.auth.isAuthenticated()) void this.init(); });
  }

  async init(): Promise<void> {
    if (this.started || !Capacitor.isNativePlatform()) return;
    this.started = true;
    try {
      let perm = await PushNotifications.checkPermissions();
      if (perm.receive === 'prompt') perm = await PushNotifications.requestPermissions();
      if (perm.receive !== 'granted') return; // user said no: in-app notifications still work
      await PushNotifications.addListener('registration', (t) => void this.registerToken(t.value));
      await PushNotifications.addListener('registrationError', () => { /* FCM not configured on this install: stay silent, in-app still works */ });
      await PushNotifications.addListener('pushNotificationReceived', (n) => void this.showArrival(n.title, n.body));
      await PushNotifications.addListener('pushNotificationActionPerformed', () => void this.router.navigateByUrl('/app/notifications'));
      await PushNotifications.register();
    } catch { /* push unavailable on this device/build: never block the app over it */ }
  }

  private async registerToken(token: string): Promise<void> {
    if (!token) return;
    try {
      await firstValueFrom(this.http.post<ApiResponse<object | null>>(`${environment.apiBaseUrl}/notifications/push-token`, {
        fcmToken: token, platform: Capacitor.getPlatform() === 'ios' ? 'Ios' : 'Android',
      }));
    } catch { /* offline: the next app start re-registers */ }
  }

  private async showArrival(title: string | undefined, body: string | undefined): Promise<void> {
    const message = [title, body].filter((x) => !!x).join(' - ');
    if (!message) return;
    const t = await this.toast.create({ message, duration: 4000, position: 'top' });
    await t.present();
  }
}
