import { Component, input } from '@angular/core';
import { IonIcon } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { flameOutline, trophyOutline, ribbonOutline, schoolOutline, peopleOutline, cashOutline, receiptOutline, timeOutline, alertCircleOutline, checkmarkDoneOutline, libraryOutline, walletOutline, statsChartOutline, calendarOutline } from 'ionicons/icons';

@Component({ selector: 'app-kpi-card', imports: [IonIcon], templateUrl: './kpi-card.component.html', styleUrl: './kpi-card.component.scss' })
export class KpiCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly tone = input<'primary' | 'warning' | 'danger'>('primary');
  readonly flat = input(false);
  readonly icon = input<string>('stats-chart-outline');
  constructor() {
    addIcons({ 'flame-outline': flameOutline, 'trophy-outline': trophyOutline, 'ribbon-outline': ribbonOutline, 'school-outline': schoolOutline, 'people-outline': peopleOutline, 'cash-outline': cashOutline,
      'receipt-outline': receiptOutline, 'time-outline': timeOutline, 'alert-circle-outline': alertCircleOutline, 'checkmark-done-outline': checkmarkDoneOutline, 'library-outline': libraryOutline,
      'wallet-outline': walletOutline, 'stats-chart-outline': statsChartOutline, 'calendar-outline': calendarOutline });
  }
}
