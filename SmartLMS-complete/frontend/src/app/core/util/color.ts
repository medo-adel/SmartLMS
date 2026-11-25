/** Brand colour maths for white-label theming. Only strict #rgb / #rrggbb values are ever written into CSS. */
export interface Rgb { r: number; g: number; b: number; }

export function parseHex(value: string | null | undefined): Rgb | null {
  if (!value) return null;
  const m = /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.exec(value.trim());
  if (!m) return null;
  let h = m[1];
  if (h.length === 3) h = h.split('').map((c) => c + c).join('');
  return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16) };
}
const clamp = (n: number) => Math.max(0, Math.min(255, Math.round(n)));
export const toHex = (c: Rgb) => '#' + [c.r, c.g, c.b].map((x) => clamp(x).toString(16).padStart(2, '0')).join('');
export const mix = (c: Rgb, to: number, amount: number): Rgb => ({ r: c.r + (to - c.r) * amount, g: c.g + (to - c.g) * amount, b: c.b + (to - c.b) * amount });
export const shade = (c: Rgb) => mix(c, 0, 0.12);
export const tint = (c: Rgb) => mix(c, 255, 0.1);

function lin(v: number): number { const s = v / 255; return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4); }
export const luminance = (c: Rgb) => 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b);
/** Text colour that stays readable on the brand colour (WCAG contrast ratio picks black or white). */
export function contrastOn(c: Rgb): Rgb {
  const l = luminance(c);
  const white = 1.05 / (l + 0.05), black = (l + 0.05) / 0.05;
  return white >= black ? { r: 255, g: 255, b: 255 } : { r: 0, g: 0, b: 0 };
}

/** Second gradient colour: the accent only when its readable text colour matches the primary's, else a darker primary. */
export function gradientEnd(primary: string | null | undefined, accent: string | null | undefined): string | null {
  const p = parseHex(primary);
  if (!p) return null;
  const q = parseHex(accent);
  if (q && toHex(contrastOn(q)) === toHex(contrastOn(p))) return toHex(q);
  return toHex(shade(p));
}

/** CSS variables for one Ionic colour, or null when the input is not a valid hex colour. */
export function ionColorVars(name: string, hex: string | null | undefined): Record<string, string> | null {
  const c = parseHex(hex);
  if (!c) return null;
  const ct = contrastOn(c), rgb = (x: Rgb) => `${x.r}, ${x.g}, ${x.b}`;
  return {
    [`--ion-color-${name}`]: toHex(c), [`--ion-color-${name}-rgb`]: rgb(c),
    [`--ion-color-${name}-contrast`]: toHex(ct), [`--ion-color-${name}-contrast-rgb`]: rgb(ct),
    [`--ion-color-${name}-shade`]: toHex(shade(c)), [`--ion-color-${name}-tint`]: toHex(tint(c)),
  };
}
