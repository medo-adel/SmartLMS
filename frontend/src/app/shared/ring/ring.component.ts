import { Component, computed, input } from '@angular/core';

/** Circular progress. Decorative number is shown inside; pass a text alternative via `label`. */
@Component({
  selector: 'app-ring',
  template: `<div class="r" role="img" [attr.aria-label]="label() + ' ' + pct() + '%'" [style.--s]="size()">
    <svg viewBox="0 0 100 100" aria-hidden="true"><circle class="t" cx="50" cy="50" r="42" /><circle class="v" cx="50" cy="50" r="42" pathLength="100" [attr.stroke-dasharray]="pct() + ' 100'" /></svg>
    <b aria-hidden="true">{{ pct() }}<small>%</small></b></div>`,
  styles: [`:host{display:inline-block}.r{position:relative;inline-size:calc(var(--s) * 1rem);block-size:calc(var(--s) * 1rem)}
    svg{inline-size:100%;block-size:100%;transform:rotate(-90deg)}:host-context([dir=rtl]) svg{transform:rotate(-90deg) scaleY(-1)}
    circle{fill:none;stroke-width:10;stroke-linecap:round}.t{stroke:var(--app-chip)}.v{stroke:var(--ion-color-primary)}
    b{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;font-size:calc(var(--s) * .3rem);font-weight:800;color:var(--app-text);direction:ltr;font-variant-numeric:tabular-nums}
    small{font-size:.5em;opacity:.6;margin-inline-start:.05em}`],
})
export class RingComponent {
  readonly value = input.required<number>(); readonly size = input(6); readonly label = input('');
  readonly pct = computed(() => Math.max(0, Math.min(100, Math.round(this.value()))));
}
