import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonContent, IonButton } from '@ionic/angular';
import { I18nService } from '../../core/services/i18n.service';
import { AuthService } from '../../core/services/auth.service';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { StudentDashboardComponent } from './student/student-dashboard.component';
import { InstructorDashboardComponent } from './instructor/instructor-dashboard.component';
import { AdminDashboardComponent } from './admin/admin-dashboard.component';
import { ParentDashboardComponent } from './parent/parent-dashboard.component';

/** Picks the dashboard for the signed-in role. The API enforces the same roles on each endpoint. */
@Component({
  selector: 'app-dashboard', templateUrl: './dashboard.page.html', styleUrl: './dashboard.page.scss',
  imports: [RouterLink, IonContent, IonButton, TranslatePipe, StudentDashboardComponent, InstructorDashboardComponent, AdminDashboardComponent, ParentDashboardComponent],
})
export class DashboardPage {
  readonly auth = inject(AuthService);
  private readonly i18n = inject(I18nService);
  /** Weekday and date in the active language (Arabic uses Arabic month names). */
  get today(): string { return new Date().toLocaleDateString(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB', { weekday: 'long', day: 'numeric', month: 'long' }); }
}
