import { describe, it, expect } from 'vitest';
import { heat } from './instructor-dashboard.component';

describe('heat', () => { it('clamps drop-off percent into 0..1', () => { expect(heat(-5)).toBe(0); expect(heat(50)).toBe(0.5); expect(heat(180)).toBe(1); }); });
