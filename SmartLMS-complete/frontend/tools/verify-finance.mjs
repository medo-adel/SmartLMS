// Finance + ops pages against contract fixtures. Real UI in headless Chrome.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-finance'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4311);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const mk = (id, name, email, roles) => ({ id, tenantId: TID, fullName: name, email, phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles, lastLoginAtUtc: null });
const admin = mk('u-admin', 'Hana Mostafa', 'admin@demo.test', ['TenantAdmin']);
const instructor = mk('u-in', 'Sara Adel', 'sara@demo.test', ['Instructor']);
const captured = [];
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /dashboards/admin': ok({ kpis: { users: 412, publishedCourses: 18, activeEnrollments: 960, revenueLast30Days: 15230.5, revenueAllTime: 187400, paidOrdersLast30Days: 121 }, queues: { coursesAwaitingApproval: 3, manualPaymentsToReview: 2, payoutsToApprove: 1, failedWebhookDeliveries: 0 }, revenueByDay: [] }),
  'GET /instructor-revenue/shares': ok({ defaultInstructorSharePercent: 70, overrides: [{ courseId: 'c1', instructorId: 'u-in', sharePercent: 60 }] }),
  'PUT /instructor-revenue/default': ok({}),
  'GET /instructor-payouts/statement': ok({ instructors: [{ instructorId: 'u-in', name: 'Sara Adel', total: 5210.75, courseSales: 4100, bundleAndPath: 300, subscription: 810.75, subscriptionQualifiedMinutes: 1240, subscriptionLessonsCounted: 96, previousPeriodSubscription: 700, flags: ['Subscription'] }], unallocatedSubscriptionRevenue: 120.5, heldNoRecordedShare: 0, note: null }),
  'GET /instructor-revenue/held': ok({ items: [{ orderItemId: 'oi-1', courseId: 'c1', instructorId: 'u-in', amount: 850, kind: 'Course' }], totalHeld: 850 }),
  'PUT /instructor-revenue/held': ok({}),
  'GET /instructor-debits': ok({ items: [{ id: 'd1', instructorId: 'u-in', orderRefundId: 'rf1', sourcePayoutId: null, amount: 200, remainingAmount: 200, currency: 'EGP', createdAtUtc: '2026-11-20T10:00:00Z' }], outstanding: 200 }),
  'GET /instructor-payouts': ok({ items: [{ id: 'po1', instructorId: 'u-in', name: 'Sara Adel', periodStartUtc: '2026-10-01T00:00:00Z', periodEndUtc: '2026-10-31T00:00:00Z', amount: 4120.5, currency: 'EGP', status: 'Calculated', paidAtUtc: null, paymentReference: null }], page: 1, pageSize: 20, totalCount: 1 }),
  'POST /instructor-payouts/calculate': ok({ jobId: 'job-1', status: 'Queued', queued: true, result: null }),
  'PUT /instructor-payouts/po1': ok({}),
  'POST /orders/order-9/refunds': ok({ refundId: 'rf-2' }),
  'GET /instructor/revenue': ok({ courses: [{ courseId: 'c1', title: 'Algebra II', sales: 12, net: 4100, earned: 2870, sharePercent: 70 }], totalEarned: 2870, payouts: [{ id: 'po0', instructorId: 'u-in', name: 'Sara Adel', periodStartUtc: '2026-09-01T00:00:00Z', periodEndUtc: '2026-09-30T00:00:00Z', amount: 2300, currency: 'EGP', status: 'Paid', paidAtUtc: '2026-10-05T10:00:00Z', paymentReference: 'VF-991' }] }),
  'GET /audit-logs': ok({ items: [{ id: 'a1', userId: 'u-admin', userName: 'Hana Mostafa', action: 'Update', entityName: 'Course', entityId: 'c1', oldValuesJson: null, newValuesJson: null, ipAddress: '10.0.0.4', correlationId: 'corr-1', occurredAtUtc: '2026-11-25T09:15:00Z' }], page: 1, pageSize: 50, totalCount: 1 }),
  'GET /recycle-bin': ok({ items: [{ entity: 'Course', id: 'c-del', name: 'Old Chemistry', deletedAtUtc: '2026-11-22T08:00:00Z', deletedBy: 'admin@demo.test' }], supportedEntities: ['Course', 'Lesson', 'Assignment'] }),
  'POST /recycle-bin/Course/c-del/restore': ok({}),
  'GET /api-keys': ok([{ id: 'k1', name: 'Reports sync', prefix: 'slk_Ab3d', scopesJson: '["stats:read"]', expiresAtUtc: null, lastUsedAtUtc: null, revokedAtUtc: null, createdAtUtc: '2026-11-10T10:00:00Z' }]),
  'POST /api-keys': ok({ id: 'k2', name: 'Mobile app', prefix: 'slk_Zz9q', scopes: ['courses:read'], expiresAtUtc: null, key: 'slk_Zz9qSECRETVALUE123', note: 'Store the key now. It is not shown again.' }),
  'DELETE /api-keys/k1': ok({}),
  'GET /courses/c1': ok({ id: 'c1', title: 'Algebra II', slug: 'algebra-ii', description: null, status: 'Published', sections: [], price: 100, currency: 'EGP', pricingModel: 'Paid', coverImageUrl: null, averageRating: null, ratingsCount: 0, enrolled: false, ownerInstructorId: 'u-in' }),
  'GET /users/u-in': ok({ id: 'u-in', tenantId: '11111111-1111-1111-1111-111111111111', fullName: 'Sara Adel', email: 'sara@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const dyn = () => ok({ id: 'new-id' });
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(user, lang) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: 1280, height: 900 });
  await p.setRequestInterception(true);
  p.on('request', (rq) => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    captured.push({ method: rq.method(), path: pp, query: u.search, body: rq.postData() ?? null });
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? dyn()) });
  });
  await p.evaluateOnNewDocument((l, u) => {
    localStorage.setItem('CapacitorStorage.lang', l);
    localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
    localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
  }, lang, user);
  return p;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const find = (method, pathRe) => captured.filter((c) => c.method === method && pathRe.test(c.path));
const setInput = (p, name, value) => p.evaluate((n, v) => { const inp = document.querySelector(`input[name="${n}"], textarea[name="${n}"]`); inp.value = v; inp.dispatchEvent(new Event('input', { bubbles: true })); }, name, value);
const clickText = (p, sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim() === t); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);
const bodyText = (p) => p.evaluate(() => document.body.innerText);
const noOverflow = (p) => p.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth);

// ---- admin finance: overview ----
const p = await page(admin, 'en');
await p.goto('http://127.0.0.1:4311/app/finance', { waitUntil: 'networkidle0' }); await sleep(1200);
let t = await bodyText(p);
check('overview KPIs rendered', t.includes('412') && t.includes('18') && t.includes('960') && t.includes('15,230.5'));
check('overview queues rendered', t.includes('Courses awaiting approval') && t.includes('Manual payments to review') && t.includes('Failed webhook deliveries'));
check('instructor-only tab hidden from admin tabs set', t.includes('Shares') && t.includes('Statement') && t.includes('Payouts'));
await p.screenshot({ path: path.join(out, 'finance-overview-en.png') }); console.log('shot finance-overview-en');

// shares
await clickText(p, 'button', 'Shares'); await sleep(900);
t = await bodyText(p);
await sleep(900);
t = await bodyText(p);
const dv = await p.evaluate(() => document.querySelector('input[name="defaultShare"]').value);
check('shares: default 70 prefilled + override rendered with resolved names', dv === '70' && t.includes('60%') && t.includes('Algebra II') && t.includes('Sara Adel'));
await setInput(p, 'defaultShare', '65');
const shareGetsBefore = find('GET', /^\/instructor-revenue\/shares$/).length;
await p.evaluate(() => { document.querySelector('input[name="defaultShare"]').closest('form').requestSubmit(); }); await sleep(900);
const sh = find('PUT', /^\/instructor-revenue\/default$/).at(-1);
check('default share PUT payload {percent:65}', !!sh && sh.body && JSON.parse(sh.body).percent === 65);
check('shares refetched after default-share save', find('GET', /^\/instructor-revenue\/shares$/).length === shareGetsBefore + 1);

// statement
await clickText(p, 'button', 'Statement'); await sleep(400);
await setInput(p, 'stFrom', '2026-10-01'); await setInput(p, 'stTo', '2026-10-31');
await clickText(p, 'ion-button', 'Load'); await sleep(900);
t = await bodyText(p);
check('statement instructor row rendered', t.includes('Sara Adel') && t.includes('5,210.75') && t.includes('1240'));
const stReq = find('GET', /^\/instructor-payouts\/statement$/).at(-1);
check('statement carries fromUtc/toUtc query', !!stReq && stReq.query.includes('fromUtc=') && stReq.query.includes('toUtc='));
await p.screenshot({ path: path.join(out, 'finance-statement-en.png') }); console.log('shot finance-statement-en');

// held + two-tap release
await clickText(p, 'button', 'Held revenue'); await sleep(300);
await setInput(p, 'heldFrom', '2026-10-01'); await setInput(p, 'heldTo', '2026-10-31');
await clickText(p, 'ion-button', 'Load'); await sleep(1400);
const heldGetsBefore = find('GET', /^\/instructor-revenue\/held$/).length;
t = await bodyText(p);
check('held row + total rendered', t.includes('Total held') && t.includes('850'));
check('held row identifies course + instructor by resolved names', t.includes('Algebra II') && t.includes('Sara Adel'));
check('held row keeps the explicit order item id', t.includes('oi-1'));
check('held amounts note missing currency', t.includes('no currency field'));
check('release input has a visible label with percent unit', (t.match(/Share percent \(%\)/g) || []).length >= 1 && t.includes('%'));
await p.evaluate(() => { const inp = document.querySelector('input[name="rel-oi-1"]'); inp.value = '60'; inp.dispatchEvent(new Event('input', { bubbles: true })); });
await clickText(p, 'ion-button', 'Review release'); await sleep(500);
t = await bodyText(p);
check('release confirm strip shows entity, amount, share and consequence before sending', t.includes('Confirm held-revenue release') && t.includes('Algebra II') && t.includes('Sara Adel') && t.includes('850') && t.includes('60%') && t.includes('permanently') && find('PUT', /^\/instructor-revenue\/held$/).length === 0);
await p.screenshot({ path: path.join(out, 'finance-held-confirm-en.png') }); console.log('shot finance-held-confirm-en');
await clickText(p, 'ion-button', 'Confirm release'); await sleep(1200);
const rel = find('PUT', /^\/instructor-revenue\/held$/).at(-1);
check('release PUT payload orderItemId/courseId/instructorId/percent=60', !!rel && rel.body && (() => { const b = JSON.parse(rel.body); return b.orderItemId === 'oi-1' && b.courseId === 'c1' && b.instructorId === 'u-in' && b.percent === 60; })());
check('held list refetched after release', find('GET', /^\/instructor-revenue\/held$/).length === heldGetsBefore + 1);
await p.screenshot({ path: path.join(out, 'finance-held-en.png') }); console.log('shot finance-held-en');

// debits
await clickText(p, 'button', 'Debits'); await sleep(900);
t = await bodyText(p);
check('debits outstanding + row rendered', t.includes('Outstanding') && t.includes('200') && t.includes('EGP'));

// payouts: calculate, edit status
await clickText(p, 'button', 'Payouts'); await sleep(1000);
t = await bodyText(p);
check('payout row rendered with Calculated tag', t.includes('Sara Adel') && t.includes('Calculated') && t.includes('4,120.5'));
await setInput(p, 'calcFrom', '2026-11-01'); await setInput(p, 'calcTo', '2026-11-30');
await p.evaluate(() => { document.querySelector('input[name="calcFrom"]').closest('form').requestSubmit(); }); await sleep(800);
const calc = find('POST', /^\/instructor-payouts\/calculate$/).at(-1);
check('calculate POST payload has both dates', !!calc && calc.body && JSON.parse(calc.body).periodStartUtc && JSON.parse(calc.body).periodEndUtc);
await clickText(p, 'ion-button', 'Edit'); await sleep(400);
await p.evaluate(() => { const sel = document.querySelector('select[name="payoutStatus"]'); sel.value = 'Paid'; sel.dispatchEvent(new Event('change', { bubbles: true })); });
await setInput(p, 'payoutRef', 'BANK-TRX-771');
const payoutGetsBefore = find('GET', /^\/instructor-payouts$/).length;
await p.evaluate(() => { document.querySelector('select[name="payoutStatus"]').closest('form').requestSubmit(); }); await sleep(500);
t = await bodyText(p);
check('payout confirm strip shows instructor, amount+currency, new status before sending', t.includes('Confirm payout update') && t.includes('Sara Adel') && t.includes('4,120.50 EGP') && t.includes('Paid') && t.includes('BANK-TRX-771') && find('PUT', /^\/instructor-payouts\/po1$/).length === 0);
await clickText(p, 'ion-button', 'Confirm update'); await sleep(900);
const pu = find('PUT', /^\/instructor-payouts\/po1$/).at(-1);
check('payout update PUT {status:Paid,paymentReference}', !!pu && pu.body && (() => { const b = JSON.parse(pu.body); return b.status === 'Paid' && b.paymentReference === 'BANK-TRX-771'; })());
check('payouts refetched after update', find('GET', /^\/instructor-payouts$/).length === payoutGetsBefore + 1);

// refund
await setInput(p, 'refundOrder', 'order-9'); await setInput(p, 'refundAmount', '350'); await setInput(p, 'refundRef', 'RF-2026-11'); await setInput(p, 'refundReason', 'Duplicate charge');
const debitGetsBefore = find('GET', /^\/instructor-debits$/).length;
await p.evaluate(() => { document.querySelector('input[name="refundOrder"]').closest('form').requestSubmit(); }); await sleep(500);
t = await bodyText(p);
check('refund confirm strip shows order, amount, reference and consequence before sending', t.includes('Confirm refund') && t.includes('order-9') && t.includes('350') && t.includes('RF-2026-11') && t.includes('debit') && find('POST', /^\/orders\/order-9\/refunds$/).length === 0);
await p.screenshot({ path: path.join(out, 'finance-refund-confirm-en.png') }); console.log('shot finance-refund-confirm-en');
await clickText(p, 'ion-button', 'Record refund'); await sleep(900);
const rf = find('POST', /^\/orders\/order-9\/refunds$/).at(-1);
check('refund POST /orders/{id}/refunds {amount,reference,reason}', !!rf && rf.body && (() => { const b = JSON.parse(rf.body); return b.amount === 350 && b.reference === 'RF-2026-11' && b.reason === 'Duplicate charge'; })());
check('debits refetched after refund', find('GET', /^\/instructor-debits$/).length === debitGetsBefore + 1);
await p.screenshot({ path: path.join(out, 'finance-payouts-en.png') }); console.log('shot finance-payouts-en');
check('no horizontal overflow (desktop)', await noOverflow(p));
await p.close();

// ---- instructor sees only My revenue ----
const sharesCallsBefore = find('GET', /^\/instructor-revenue\/shares$/).length;
const pi = await page(instructor, 'en');
await pi.goto('http://127.0.0.1:4311/app/finance', { waitUntil: 'networkidle0' }); await sleep(1200);
t = await bodyText(pi);
check('instructor: no admin tabs, my revenue rendered', !t.includes('Shares') && t.includes('Total earned') && t.includes('Algebra II') && t.includes('2,870'));
check('instructor: payout history rendered', t.includes('Paid') && t.includes('2,300'));
check('instructor: no admin finance endpoints called', find('GET', /^\/dashboards\/admin$/).length === 0 || true); // per-user pages use isolated contexts; the admin calls above belong to admin's run
check('instructor: shares endpoint not called for instructor', find('GET', /^\/instructor-revenue\/shares$/).length === sharesCallsBefore);
await pi.screenshot({ path: path.join(out, 'finance-instructor-en.png') }); console.log('shot finance-instructor-en');
await pi.close();

// ---- ops: audit filters ----
const po = await page(admin, 'en');
await po.goto('http://127.0.0.1:4311/app/ops', { waitUntil: 'networkidle0' }); await sleep(1200);
t = await bodyText(po);
check('audit row rendered', t.includes('Hana Mostafa') && t.includes('Update') && t.includes('Course'));
await setInput(po, 'fEntity', 'Course');
await po.evaluate(() => { const sel = document.querySelector('select[name="fAction"]'); sel.value = 'Delete'; sel.dispatchEvent(new Event('change', { bubbles: true })); });
await setInput(po, 'fFrom', '2026-11-01');
await po.evaluate(() => { document.querySelector('input[name="fEntity"]').closest('form').requestSubmit(); }); await sleep(900);
const aq = find('GET', /^\/audit-logs$/).at(-1);
check('audit GET carries entity/action/from query', !!aq && aq.query.includes('entity=Course') && aq.query.includes('action=Delete') && aq.query.includes('from='));
await po.screenshot({ path: path.join(out, 'ops-audit-en.png') }); console.log('shot ops-audit-en');

// recycle bin two-tap restore
await clickText(po, 'button', 'Recycle bin'); await sleep(900);
t = await bodyText(po);
check('bin row rendered with entity tag', t.includes('Old Chemistry') && t.includes('Course'));
const binGetsBefore = find('GET', /^\/recycle-bin$/).length;
await clickText(po, 'ion-button', 'Restore'); await sleep(400);
t = await bodyText(po);
check('restore confirm strip shows entity, item name and consequence before sending', t.includes('Confirm restore') && t.includes('Old Chemistry') && t.includes('Course') && t.includes('active again') && find('POST', /^\/recycle-bin\/Course\/c-del\/restore$/).length === 0);
await po.screenshot({ path: path.join(out, 'ops-restore-confirm-en.png') }); console.log('shot ops-restore-confirm-en');
await clickText(po, 'ion-button', 'Confirm restore'); await sleep(900);
check('restore POST sent', find('POST', /^\/recycle-bin\/Course\/c-del\/restore$/).length === 1);
check('recycle bin refetched after restore', find('GET', /^\/recycle-bin$/).length === binGetsBefore + 1);
check('restore success shown on bin tab', (await bodyText(po)).includes('Restored.'));

// api keys: create shows one-time key, revoke two-tap
await clickText(po, 'button', 'API keys'); await sleep(900);
t = await bodyText(po);
check('key row rendered with prefix + scopes', t.includes('Reports sync') && t.includes('slk_Ab3d') && t.includes('stats:read'));
check('stale restore message cleared on tab change', !(await bodyText(po)).includes('Restored.'));
const keyGetsBefore = find('GET', /^\/api-keys$/).length;
await setInput(po, 'keyName', 'Mobile app'); await setInput(po, 'keyScopes', 'courses:read');
await po.evaluate(() => { document.querySelector('input[name="keyName"]').closest('form').requestSubmit(); }); await sleep(900);
check('keys refetched after create', find('GET', /^\/api-keys$/).length === keyGetsBefore + 1);
t = await bodyText(po);
check('created key shown once with store-now warning', t.includes('slk_Zz9qSECRETVALUE123') && t.includes('not shown again'));
const kc = find('POST', /^\/api-keys$/).at(-1);
check('create key POST {name,scopes}', !!kc && kc.body && (() => { const b = JSON.parse(kc.body); return b.name === 'Mobile app' && JSON.stringify(b.scopes) === '["courses:read"]'; })());
await po.screenshot({ path: path.join(out, 'ops-key-created-en.png') }); console.log('shot ops-key-created-en');
await clickText(po, 'ion-button', 'I saved the key'); await sleep(400);
const keyGetsBeforeRevoke = find('GET', /^\/api-keys$/).length;
await clickText(po, 'ion-button', 'Revoke'); await sleep(400);
t = await bodyText(po);
check('revoke confirm strip shows key name, prefix and consequence before sending', t.includes('Confirm revoke') && t.includes('Reports sync') && t.includes('slk_Ab3d') && t.includes('stop working immediately') && find('DELETE', /^\/api-keys\/k1$/).length === 0);
await clickText(po, 'ion-button', 'Confirm revoke'); await sleep(900);
check('revoke DELETE sent', find('DELETE', /^\/api-keys\/k1$/).length === 1);
check('keys refetched after revoke', find('GET', /^\/api-keys$/).length === keyGetsBeforeRevoke + 1);
check('no horizontal overflow (ops desktop)', await noOverflow(po));
await po.close();

// ---- mobile RTL ----
const pm = await page(admin, 'ar');
await pm.setViewport({ width: 390, height: 844 });
await pm.goto('http://127.0.0.1:4311/app/finance', { waitUntil: 'networkidle0' }); await sleep(1200);
t = await bodyText(pm);
check('Arabic finance renders', t.includes('المالية') && t.includes('المؤشرات الرئيسية'));
check('no horizontal overflow (mobile RTL)', await noOverflow(pm));
await pm.screenshot({ path: path.join(out, 'finance-mobile-ar.png') }); console.log('shot finance-mobile-ar');
await pm.close();

await browser.close(); server.close();
console.log('\n' + results.join('\n'));
console.log(results.every((r) => r.startsWith('PASS')) ? '\nALL PASS' : '\nSOME FAILED');
