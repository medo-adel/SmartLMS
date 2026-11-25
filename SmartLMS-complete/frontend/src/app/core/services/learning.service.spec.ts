import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../../../environments/environment';
import { LearningService } from './learning.service';

const API = environment.apiBaseUrl;
const ok = <T>(data: T) => ({ success: true, data, message: null, errors: [] });

describe('LearningService', () => {
  let s: LearningService, ctrl: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); s = TestBed.inject(LearningService); ctrl = TestBed.inject(HttpTestingController); });
  afterEach(() => ctrl.verify());

  it('searches the catalog with the q parameter the API reads and unwraps ApiResponse', async () => {
    const p = s.courses(' algebra ');
    const r = ctrl.expectOne((q) => q.url === `${API}/courses`);
    expect(r.request.params.get('q')).toBe('algebra');
    r.flush(ok({ totalCount: 1, page: 1, pageSize: 12, items: [{ id: 'c1', title: 'Algebra' }] }));
    expect((await p).items[0].id).toBe('c1');
  });
  it('turns success:false into an error', async () => {
    const p = s.wallet(); ctrl.expectOne(`${API}/wallet`).flush({ success: false, data: null, message: 'nope', errors: [] });
    await expect(p).rejects.toThrow('nope');
  });
  it('saves progress with the contract field names', async () => {
    const p = s.saveProgress('l1', 42, 15, false, 1.5);
    const r = ctrl.expectOne(`${API}/lessons/l1/progress`); expect(r.request.method).toBe('PUT');
    expect(r.request.body).toEqual({ lastPositionSeconds: 42, watchedSecondsDelta: 15, completed: false, playbackSpeed: 1.5 }); r.flush(ok({})); await p;
  });
  it('sends recordedAtUtc for an offline replay and leaves it out for a live heartbeat', async () => {
    const p = s.saveProgress('l1', 42, 15, true, 1, '2026-11-24T10:00:00.000Z');
    const r = ctrl.expectOne(`${API}/lessons/l1/progress`);
    expect(r.request.body).toEqual({ lastPositionSeconds: 42, watchedSecondsDelta: 15, completed: true, playbackSpeed: 1, recordedAtUtc: '2026-11-24T10:00:00.000Z' }); r.flush(ok({})); await p;
    const q = s.saveProgress('l1', 1, 1, false, 1); const r2 = ctrl.expectOne(`${API}/lessons/l1/progress`);
    expect('recordedAtUtc' in (r2.request.body as object)).toBe(false); r2.flush(ok({})); await q;
  });
  it('requests the lesson stream as a blob (bearer token is added by the interceptor)', async () => {
    const p = s.streamBlob('l1'); const r = ctrl.expectOne(`${API}/lessons/l1/stream`); expect(r.request.responseType).toBe('blob'); r.flush(new Blob(['x'])); expect((await p).size).toBe(1);
  });
  it('sends exam answers and tab-switch events to the attempt endpoints', async () => {
    const a = s.saveAnswers('a1', [{ attemptQuestionId: 'q1', answer: { optionId: 'o1' } }]);
    const r = ctrl.expectOne(`${API}/attempts/a1/answers`); expect(r.request.body).toEqual({ answers: [{ attemptQuestionId: 'q1', answer: { optionId: 'o1' } }] }); r.flush(ok({})); await a;
    const e = s.attemptEvent('a1', 'TabSwitch'); const r2 = ctrl.expectOne(`${API}/attempts/a1/events`); expect(r2.request.body).toEqual({ eventType: 'TabSwitch' }); r2.flush(ok({})); await e;
  });
  it('checkout sends the payment method and coupon; voucher only for the voucher method', async () => {
    const p = s.checkout('wallet', 'SAVE10', null); const r = ctrl.expectOne(`${API}/orders/checkout`);
    expect(r.request.body).toEqual({ paymentMethod: 'wallet', couponCode: 'SAVE10', voucherCode: null, gateway: null, referralCode: null }); r.flush(ok({})); await p;
  });
  it('voice tutor uploads multipart with the audio part', async () => {
    const p = s.voiceTutor('c1', new Blob(['a']), 'ar'); const r = ctrl.expectOne(`${API}/ai/courses/c1/voice-tutor`);
    expect(r.request.body instanceof FormData).toBe(true); expect((r.request.body as FormData).get('language')).toBe('ar'); expect((r.request.body as FormData).has('audio')).toBe(true);
    r.flush(ok({ transcript: 't', answerText: 'a', audioBase64: null, audioMime: null, citations: [] })); await p;
  });
  it('encodes path ids', async () => { const p = s.course('a/b'); ctrl.expectOne(`${API}/courses/a%2Fb`).flush(ok({})); await p; });
  it('sends a manual payment as multipart with the receipt file (the API rejects JSON)', async () => {
    const file = new File([new Uint8Array([137, 80, 78, 71])], 'r.png', { type: 'image/png' });
    const p = s.submitManualPayment({ purpose: 'Order', method: 'InstaPay', amount: 150, orderId: 'o1', senderReference: 'ref', senderPhoneOrAccount: null }, file);
    const r = ctrl.expectOne(`${API}/manual-payments`); const f = r.request.body as FormData;
    expect(f instanceof FormData).toBe(true);
    expect(f.get('purpose')).toBe('Order'); expect(f.get('orderId')).toBe('o1'); expect(f.get('amount')).toBe('150');
    expect((f.get('receipt') as File).name).toBe('r.png'); expect(f.has('senderPhoneOrAccount')).toBe(false);
    r.flush(ok({ id: 'm1' })); expect((await p).id).toBe('m1');
  });
  it('checkout passes the gateway only when given, and gateway top-up posts amount and gateway', async () => {
    const p = s.checkout('gateway', null, null, 'Fawry'); const r = ctrl.expectOne(`${API}/orders/checkout`);
    expect(r.request.body.gateway).toBe('Fawry'); r.flush(ok({ orderId: 'o', status: 'Pending', total: 10, currency: 'EGP', checkoutUrl: 'https://x.test/pay' })); expect((await p).checkoutUrl).toContain('https://');
    const t = s.topUpGateway(50, 'Paymob'); const r2 = ctrl.expectOne(`${API}/wallet/topup/gateway`);
    expect(r2.request.body).toEqual({ amount: 50, gateway: 'Paymob' }); r2.flush(ok({ paymentId: 'p', checkoutUrl: 'https://x.test', reference: 'r' })); await t;
  });
  it('builder calls use the contract routes and bodies', async () => {
    const a = s.orderLessons('s1', ['b', 'a']); const r = ctrl.expectOne(`${API}/sections/s1/lessons/order`);
    expect(r.request.method).toBe('PUT'); expect(r.request.body).toEqual({ ids: ['b', 'a'] }); r.flush(ok({})); await a;
    const c = s.setChapters('l1', [{ title: 'Intro', startSeconds: 0 }]); const r2 = ctrl.expectOne(`${API}/lessons/l1/chapters`);
    expect(r2.request.body).toEqual({ chapters: [{ title: 'Intro', startSeconds: 0 }] }); r2.flush(ok({})); await c;
    const d = s.deleteLesson('l9'); const r3 = ctrl.expectOne(`${API}/lessons/l9`); expect(r3.request.method).toBe('DELETE'); r3.flush(ok({})); await d;
    const u = s.uploadResource('l1', new File(['x'], 'n.pdf'), 'Notes', true); const r4 = ctrl.expectOne(`${API}/lessons/l1/resources`);
    expect((r4.request.body as FormData).get('isDownloadable')).toBe('true'); r4.flush(ok({})); await u;
  });
  it('admin review and voucher calls use the contract routes', async () => {
    const a = s.rejectManual('m1', 'blurry'); const r = ctrl.expectOne(`${API}/manual-payments/m1/reject`); expect(r.request.body).toEqual({ reason: 'blurry' }); r.flush(ok({})); await a;
    const b = s.approveManual('m2'); ctrl.expectOne(`${API}/manual-payments/m2/approve`).flush(ok({})); await b;
    const c = s.createVoucherBatch({ name: 'n', prefix: null, quantity: 5, voucherType: 'WalletCredit', courseId: null, walletAmount: 50, expiresAtUtc: null }); const r3 = ctrl.expectOne(`${API}/voucher-batches`); expect(r3.request.body.walletAmount).toBe(50); r3.flush(ok({})); await c;
    const q = s.reviewQueue('Pending'); const r4 = ctrl.expectOne((x) => x.url === `${API}/manual-payments`); expect(r4.request.params.get('status')).toBe('Pending'); r4.flush(ok({ items: [], totalCount: 0, page: 1, pageSize: 20 })); await q;
  });

  it('monetization calls hit the real routes with the expected bodies', async () => {
    const a = s.createCoupon({ code: 'X', discountType: 'Percentage', value: 10, maxRedemptions: null, maxPerUser: 1, minOrderAmount: null, expiresAtUtc: null, courseId: null, isActive: true }); const r1 = ctrl.expectOne(`${API}/coupons`); expect(r1.request.body.code).toBe('X'); r1.flush(ok({ id: 'c' })); await a;
    const b = s.createBundle({ name: 'B', description: null, price: 99, currency: 'EGP', courseIds: ['1', '2'] }); const r2 = ctrl.expectOne(`${API}/bundles`); expect(r2.request.body.courseIds.length).toBe(2); r2.flush(ok({ id: 'b' })); await b;
    const c = s.createAffiliate('u', 10, null); const r3 = ctrl.expectOne(`${API}/affiliate/profiles`); expect(r3.request.body.commissionPercent).toBe(10); r3.flush(ok({})); await c;
    const d = s.setPricing('c1', 'OneTime', 50, 'EGP', null); const r4 = ctrl.expectOne(`${API}/courses/c1/pricing`); expect(r4.request.method).toBe('PUT'); r4.flush(ok({})); await d;
    const e = s.setLessonPrice('l1', null); const r5 = ctrl.expectOne(`${API}/lessons/l1/price`); expect(r5.request.method).toBe('PUT'); r5.flush(ok({})); await e;
  });
});
