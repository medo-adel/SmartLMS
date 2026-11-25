import { Component, computed, input } from '@angular/core';

export interface RadarDatum { label: string; value: number; }

/** SVG radar (spider) chart for values 0-100. Needs 3+ axes; fewer renders a plain list in the page. */
@Component({ selector: 'app-radar-chart', templateUrl: './radar-chart.component.html', styleUrl: './radar-chart.component.scss' })
export class RadarChartComponent {
  readonly data = input.required<RadarDatum[]>();
  private readonly R = 38;
  private pt(i: number, n: number, v: number) { const a = (Math.PI * 2 * i) / n - Math.PI / 2; const r = (this.R * Math.max(0, Math.min(100, v))) / 100; return { x: 50 + r * Math.cos(a), y: 50 + r * Math.sin(a) }; }
  readonly shape = computed(() => this.data().map((d, i, a) => this.pt(i, a.length, d.value)).map((p) => `${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' '));
  readonly rings = computed(() => [25, 50, 75, 100].map((v) => this.data().map((_, i, a) => this.pt(i, a.length, v)).map((p) => `${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' ')));
  readonly axes = computed(() => this.data().map((d, i, a) => ({ end: this.pt(i, a.length, 100), label: this.pt(i, a.length, 122), text: d.label })));
}
