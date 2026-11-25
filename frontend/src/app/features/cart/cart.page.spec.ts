import { describe, it, expect } from 'vitest';
import { outcomeState, safeCheckoutUrl } from './cart.page';
import { BuilderPage } from '../builder/builder.page';

describe('checkout outcome', () => {
  it('reads paid, gateway and manual outcomes', () => {
    expect(outcomeState({ orderId: '1', status: 'Paid', total: 5, currency: 'EGP' })).toBe('paid');
    expect(outcomeState({ orderId: '1', status: 'Pending', total: 5, currency: 'EGP', checkoutUrl: 'https://pay.test/c/1' })).toBe('gateway');
    expect(outcomeState({ orderId: '1', status: 'AwaitingManualReview', total: 5, currency: 'EGP', next: 'x' })).toBe('manual');
    expect(outcomeState({ orderId: '1', status: 'Pending', total: 5, currency: 'EGP' })).toBe('other');
  });
  it('refuses non-web checkout links', () => {
    expect(safeCheckoutUrl('javascript:alert(1)')).toBeNull();
    expect(safeCheckoutUrl('not a url')).toBeNull();
    expect(safeCheckoutUrl(undefined)).toBeNull();
    expect(safeCheckoutUrl('https://pay.test/x')).toBe('https://pay.test/x');
  });
  it('a javascript link never makes a gateway state', () => {
    expect(outcomeState({ orderId: '1', status: 'Pending', total: 5, currency: 'EGP', checkoutUrl: 'javascript:alert(1)' })).toBe('other');
  });
});
describe('builder reorder', () => {
  it('moves an item and stops at the ends', () => {
    expect(BuilderPage.moved(['a', 'b', 'c'], 1, -1)).toEqual(['b', 'a', 'c']);
    expect(BuilderPage.moved(['a', 'b', 'c'], 2, 1)).toBeNull();
    expect(BuilderPage.moved(['a', 'b', 'c'], 0, -1)).toBeNull();
  });
});
