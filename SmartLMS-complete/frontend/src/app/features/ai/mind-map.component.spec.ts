import { describe, it, expect } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { MindMapComponent } from './mind-map.component';

describe('MindMapComponent layout', () => {
  it('puts the root on the first level and children below, one rect per node and one line per edge', () => {
    const f = TestBed.createComponent(MindMapComponent);
    f.componentRef.setInput('map', { nodes: [{ id: 'r', label: 'Root' }, { id: 'a', label: 'A' }, { id: 'b', label: 'B' }, { id: 'c', label: 'C' }], edges: [{ source: 'r', target: 'a' }, { source: 'r', target: 'b' }, { source: 'a', target: 'c' }] });
    f.detectChanges();
    const l = f.componentInstance.layout(); const y = (id: string) => l.nodes.find((n) => n.id === id)!.y;
    expect(y('r')).toBeLessThan(y('a')); expect(y('a')).toBe(y('b')); expect(y('c')).toBeGreaterThan(y('a'));
    expect(f.nativeElement.querySelectorAll('rect').length).toBe(4); expect(f.nativeElement.querySelectorAll('line').length).toBe(3);
  });
  it('survives a cycle and unconnected nodes', () => {
    const f = TestBed.createComponent(MindMapComponent);
    f.componentRef.setInput('map', { nodes: [{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }, { id: 'z', label: 'Z' }], edges: [{ source: 'a', target: 'b' }, { source: 'b', target: 'a' }] });
    f.detectChanges(); expect(f.componentInstance.layout().nodes.length).toBe(3);
  });
});
