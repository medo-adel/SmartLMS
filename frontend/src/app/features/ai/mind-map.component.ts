import { Component, computed, input } from '@angular/core';
import { AiMindMap } from '../../core/models/learning';

interface Placed { id: string; label: string; x: number; y: number; }

/** Interactive-free SVG viewer: nodes are laid out in levels from the root(s) found by the edges. */
@Component({ selector: 'app-mind-map', templateUrl: './mind-map.component.html', styleUrl: './mind-map.component.scss' })
export class MindMapComponent {
  readonly map = input.required<AiMindMap>();
  readonly width = 480;
  readonly layout = computed(() => {
    const m = this.map();
    const incoming = new Set(m.edges.map((e) => e.target));
    const roots = m.nodes.filter((n) => !incoming.has(n.id)).map((n) => n.id);
    const level = new Map<string, number>();
    let frontier = roots.length ? roots : m.nodes.slice(0, 1).map((n) => n.id);
    frontier.forEach((id) => level.set(id, 0));
    while (frontier.length) {
      const next: string[] = [];
      for (const id of frontier) for (const e of m.edges) if (e.source === id && !level.has(e.target)) { level.set(e.target, (level.get(id) ?? 0) + 1); next.push(e.target); }
      frontier = next;
    }
    const maxLevel = Math.max(0, ...level.values());
    m.nodes.forEach((n) => { if (!level.has(n.id)) level.set(n.id, maxLevel + 1); });
    const levels = new Map<number, string[]>();
    m.nodes.forEach((n) => { const l = level.get(n.id) ?? 0; levels.set(l, [...(levels.get(l) ?? []), n.id]); });
    const placed = new Map<string, Placed>();
    for (const n of m.nodes) {
      const l = level.get(n.id) ?? 0, row = levels.get(l) ?? [], i = row.indexOf(n.id);
      placed.set(n.id, { id: n.id, label: n.label, x: ((i + 1) * this.width) / (row.length + 1), y: 40 + l * 90 });
    }
    const height = 80 + (Math.max(0, ...levels.keys()) + 1) * 90 - 40;
    const lines = m.edges.flatMap((e) => { const a = placed.get(e.source), b = placed.get(e.target); return a && b ? [{ x1: a.x, y1: a.y, x2: b.x, y2: b.y }] : []; });
    return { nodes: [...placed.values()], lines, height };
  });
  short(s: string): string { return s.length > 18 ? s.slice(0, 17) + '…' : s; }
}
