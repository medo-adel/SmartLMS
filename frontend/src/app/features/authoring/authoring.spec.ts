import { clock } from '../player/lesson-extras.component';
import { QUESTION_TYPES, buildQuestion } from './authoring.page';

describe('authoring and player helpers', () => {
  it('lists all seven backend question types', () => { expect(QUESTION_TYPES.length).toBe(7); expect(QUESTION_TYPES).toContain('Matching'); });
  it('formats timestamps as m:ss', () => { expect(clock(0)).toBe('0:00'); expect(clock(65.9)).toBe('1:05'); expect(clock(-3)).toBe('0:00'); });
  it('validates question shapes before sending', () => {
    const base = { type: 'McqSingle', text: 'Q', points: 1, difficulty: 'Easy', topic: '', explanation: '', tf: true };
    expect(buildQuestion({ ...base, options: [{ text: 'a', isCorrect: true, matchKey: '' }, { text: 'b', isCorrect: false, matchKey: '' }] })).toMatchObject({ questionType: 'McqSingle' });
    expect(buildQuestion({ ...base, options: [{ text: 'a', isCorrect: true, matchKey: '' }, { text: 'b', isCorrect: true, matchKey: '' }] })).toBe('single');
    expect(buildQuestion({ ...base, type: 'TrueFalse', options: [] })).toMatchObject({ trueFalseAnswer: true });
    expect(buildQuestion({ ...base, text: ' ', options: [] })).toBe('text');
  });
});
