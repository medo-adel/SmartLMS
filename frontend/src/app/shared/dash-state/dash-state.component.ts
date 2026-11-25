import { Component, input, output } from '@angular/core';
import { IonButton, IonIcon } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { cloudOfflineOutline, refreshOutline } from 'ionicons/icons';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** Loading placeholder that mirrors the dashboard layout (KPI row plus two panels) so nothing jumps when data arrives. */
@Component({
  selector: 'app-dash-skeleton',
  template: `<div role="status" aria-busy="true"><span class="sr-only">{{ 'common.loading' | t }}</span>
    <div class="g">@for (i of kpis(); track i) { <div class="k"><span class="sk c"></span><span class="t"><i class="sk a"></i><i class="sk b"></i></span></div> }</div>
    <div class="p"><i class="sk h"></i><i class="sk l"></i><i class="sk l w"></i></div>
    <div class="p"><i class="sk h"></i><i class="sk big"></i></div></div>`,
  imports: [TranslatePipe],
  styles: [`:host{display:block}
    .g{display:grid;gap:.85rem;grid-template-columns:repeat(auto-fit,minmax(8.5rem,1fr));margin-block-end:1.1rem}
    @media(min-width:600px){.g{grid-template-columns:repeat(auto-fit,minmax(12rem,1fr))}}
    .k,.p{background:var(--app-card);border:1px solid var(--app-border);border-radius:var(--app-radius);padding:1rem;box-shadow:var(--app-shadow)}
    .k{display:flex;gap:.85rem;align-items:center}.c{inline-size:2.75rem;block-size:2.75rem;border-radius:14px;flex:none}
    .t{display:grid;gap:.45rem;flex:1}.a{display:block;block-size:1.2rem;inline-size:55%}.b{display:block;block-size:.75rem;inline-size:80%}
    .p{margin-block-end:1.1rem;display:grid;gap:.7rem}.h{display:block;block-size:1.1rem;inline-size:9rem}.l{display:block;block-size:.9rem;inline-size:100%}.w{inline-size:70%}.big{display:block;block-size:8rem}`],
})
export class DashSkeletonComponent { readonly count = input(4); kpis() { return Array.from({ length: this.count() }, (_, i) => i); } }

/** Error state with a retry action. Announced to screen readers. */
@Component({
  selector: 'app-dash-error',
  imports: [IonButton, IonIcon, TranslatePipe],
  template: `<div class="e" role="alert"><span class="ic"><ion-icon name="cloud-offline-outline" aria-hidden="true"></ion-icon></span>
    <h2>{{ 'dash.failed' | t }}</h2><p>{{ 'common.error' | t }}</p>
    <ion-button (click)="retry.emit()"><ion-icon slot="start" name="refresh-outline"></ion-icon>{{ 'common.retry' | t }}</ion-button></div>`,
  styles: [`:host{display:block}.e{display:grid;justify-items:center;text-align:center;gap:.4rem;padding:2.5rem 1.25rem;background:var(--app-card);border:1px dashed var(--app-border-strong);border-radius:var(--app-radius)}
    .ic{display:grid;place-items:center;inline-size:3.5rem;block-size:3.5rem;border-radius:50%;background:var(--app-danger-bg);color:var(--app-danger);font-size:1.7rem}
    h2{margin:.5rem 0 0;font-size:1.1rem}p{margin:0 0 .75rem;color:var(--app-muted)}`],
})
export class DashErrorComponent { readonly retry = output<void>(); constructor() { addIcons({ 'cloud-offline-outline': cloudOfflineOutline, 'refresh-outline': refreshOutline }); } }
