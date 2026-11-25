import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { LearningService } from '../../core/services/learning.service';
import { AttemptQuestion } from '../../core/models/learning';
import { ExamPage } from './exam.page';

const q = (id: string, type: string): AttemptQuestion => ({ attemptQuestionId: id, type, text: 't', points: 1, savedAnswer: null, options: [{ id: 'o1', text: 'a' }, { id: 'o2', text: 'b' }], matchTargets: ['x', 'y'] });

describe('ExamPage answer handling', () => {
  let page: ExamPage; const calls: unknown[][] = [];
  beforeEach(() => {
    calls.length = 0;
    TestBed.configureTestingModule({ providers: [{ provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'e1' } } } },
      { provide: LearningService, useValue: { saveAnswers: async (...a: unknown[]) => { calls.push(a); return {}; } } }] });
    page = TestBed.runInInjectionContext(() => new ExamPage());
  });
  it('single choice keeps one option; multi choice toggles', () => {
    const single = q('a', 'McqSingle'), multi = q('b', 'McqMulti');
    page.setSingle(single, 'o1'); page.setSingle(single, 'o2'); expect(page.answers()['a']).toEqual({ optionId: 'o2' }); expect(page.isChecked(single, 'o1')).toBe(false);
    page.toggle(multi, 'o1'); page.toggle(multi, 'o2'); page.toggle(multi, 'o1'); expect(page.answers()['b']).toEqual({ optionIds: ['o2'] });
  });
  it('text and matching answers use the shapes the grader reads (text, pairs by option id)', () => {
    const t = q('c', 'ShortAnswer'), m = q('d', 'Matching');
    page.setText(t, 'hello'); expect(page.answers()['c']).toEqual({ text: 'hello' }); expect(page.isText(t)).toBe(true);
    page.setPair(m, 'o1', 'x'); page.setPair(m, 'o2', 'y'); expect(page.answers()['d']).toEqual({ pairs: { o1: 'x', o2: 'y' } });
  });
  it('autosave sends every answer to the API', async () => {
    page.attempt.set({ attemptId: 'at1', expiresAtUtc: null, remainingSeconds: null, questions: [] });
    page.setSingle(q('a', 'McqSingle'), 'o1'); await page.save();
    expect(calls[0]).toEqual(['at1', [{ attemptQuestionId: 'a', answer: { optionId: 'o1' } }]]);
  });
  it('formats the countdown', () => { page.left.set(125); expect(page.clock()).toBe('2:05'); });
});
