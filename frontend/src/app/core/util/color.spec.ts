import { describe, it, expect } from 'vitest';
import { parseHex, toHex, contrastOn, ionColorVars } from './color';

describe('brand colour maths', () => {
  it('parses 3 and 6 digit hex', () => { expect(parseHex('#fff')).toEqual({ r: 255, g: 255, b: 255 }); expect(parseHex('#0a1B2c')).toEqual({ r: 10, g: 27, b: 44 }); });
  it('rejects anything that is not strict hex (CSS injection guard)', () => {
    for (const bad of ['red', 'url(http://x)', '#12', '#gggggg', '#fff; background:url(x)', 'var(--x)', '', null, undefined]) expect(parseHex(bad as string)).toBeNull();
    expect(ionColorVars('primary', 'expression(alert(1))')).toBeNull();
  });
  it('picks readable contrast text', () => { expect(toHex(contrastOn(parseHex('#ffffff')!))).toBe('#000000'); expect(toHex(contrastOn(parseHex('#0b1f4d')!))).toBe('#ffffff'); });
  it('emits the full Ionic variable set', () => {
    const v = ionColorVars('primary', '#336699')!;
    expect(Object.keys(v).sort()).toEqual(['--ion-color-primary', '--ion-color-primary-contrast', '--ion-color-primary-contrast-rgb', '--ion-color-primary-rgb', '--ion-color-primary-shade', '--ion-color-primary-tint']);
    expect(v['--ion-color-primary']).toBe('#336699'); expect(v['--ion-color-primary-rgb']).toBe('51, 102, 153');
  });
});

import { gradientEnd } from './color';
describe('gradientEnd', () => {
  it('uses the accent only when its readable text colour matches the primary', () => {
    expect(gradientEnd('#0f766e', '#7c3aed')).toBe('#7c3aed'); // both need white text
    expect(gradientEnd('#0f766e', '#ffe066')).not.toBe('#ffe066'); // bright accent needs black text: fall back to darker primary
    expect(gradientEnd('#0f766e', 'javascript:alert(1)')).toMatch(/^#[0-9a-f]{6}$/);
    expect(gradientEnd(null, '#7c3aed')).toBeNull();
  });
});
