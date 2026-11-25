import { Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';

export interface BarDatum { label: string; value: number; }

/** Dependency-free column chart (HTML/CSS, so labels stay crisp and follow the theme). */
@Component({ selector: 'app-bar-chart', imports: [DecimalPipe], templateUrl: './bar-chart.component.html', styleUrl: './bar-chart.component.scss' })
export class BarChartComponent {
  readonly data = input.required<BarDatum[]>();
  readonly ariaLabel = input<string>('chart');
  readonly max = computed(() => Math.max(1, ...this.data().map((x) => x.value)));
  readonly bars = computed(() => this.data().map((x) => ({ ...x, pct: (x.value / this.max()) * 100 })));
}
