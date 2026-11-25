import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { EN } from './en';
import { AR } from './ar';
import { I18nService } from '../services/i18n.service';

describe('translations', () => {
  it('Arabic has exactly the English keys', () => { expect(Object.keys(AR).sort()).toEqual(Object.keys(EN).sort()); });
  it('no empty strings', () => { for (const d of [EN, AR]) for (const [k, v] of Object.entries(d)) expect(v.trim().length, k).toBeGreaterThan(0); });
});

describe('I18nService', () => {
  let s: I18nService;
  beforeEach(() => { localStorage.clear(); TestBed.configureTestingModule({}); s = TestBed.inject(I18nService); });
  it('switches html dir and lang between LTR English and RTL Arabic', async () => {
    s.apply('en'); expect(document.documentElement.getAttribute('dir')).toBe('ltr');
    await s.set('ar'); expect(document.documentElement.getAttribute('dir')).toBe('rtl'); expect(document.documentElement.getAttribute('lang')).toBe('ar');
    expect(s.t('login.title')).toBe(AR['login.title']);
    await s.set('en'); expect(document.documentElement.getAttribute('dir')).toBe('ltr'); expect(s.t('login.title')).toBe('Sign in');
  });
  it('normalizes language codes and falls back to the key', () => { expect(s.normalize('ar-EG')).toBe('ar'); expect(s.normalize('fr')).toBe('en'); expect(s.t('nope.key')).toBe('nope.key'); });
});
