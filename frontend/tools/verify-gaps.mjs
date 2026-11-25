// Gap-closure flows: change password, course voucher, subscriptions cancel, payout job polling.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-gaps'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const mk = (id, name, email, roles) => ({ id, tenantId: TID, fullName: name, email, phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles, lastLoginAtUtc: null });
const student = mk('u-st', 'Omar Khaled', 'omar@demo.test', ['Student']);
const admin = mk('u-admin', 'Hana Mostafa', 'admin@demo.test', ['TenantAdmin']);
const captured = [];
let jobPolls = 0;
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'POST /me/change-password': ok({}),
  'GET /wallet': ok({ balance: 120.5, currency: 'EGP' }),
  'GET /wallet/transactions': ok({ items: [], total: 0 }),
  'GET /manual-payments/mine': ok([]),
  'POST /vouchers/redeem': ok({ courseId: 'c1', title: 'Algebra II' }),
  'GET /orders': ok({ items: [], total: 0 }),
  'GET /affiliate/me': Object.assign(ok({}), { success: false, message: 'NOT_AN_AFFILIATE', data: null }),
  'GET /subscriptions/mine': ok([{ id: 'sub1', planName: 'Pro Monthly', price: 199, currency: 'EGP', billingPeriod: 'Monthly', status: 'Active', startedAtUtc: '2026-11-01T00:00:00Z', currentPeriodEndUtc: '2026-12-01T00:00:00Z', cancelledAtUtc: null }]),
  'POST /subscriptions/sub1/cancel': ok({}),
  'GET /dashboards/admin': ok({ kpis: { users: 1, publishedCourses: 1, activeEnrollments: 1, revenueLast30Days: 0, revenueAllTime: 0, paidOrdersLast30Days: 0 }, queues: { coursesAwaitingApproval: 0, manualPaymentsToReview: 0, payoutsToApprove: 0, failedWebhookDeliveries: 0 }, revenueByDay: [] }),
  'GET /instructor-revenue/shares': ok({ defaultInstructorSharePercent: 70, overrides: [] }),
  'GET /instructor-debits': ok({ items: [], outstanding: 0 }),
  'GET /instructor-payouts': ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
  'POST /instructor-payouts/calculate': ok({ jobId: 'job-1', status: 'Queued', queued: true, result: null }),
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
    if (pp === '/jobs/job-1') { jobPolls++; return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ id: 'job-1', type: 'PayoutCalculation', status: jobPolls >= 2 ? 'Succeeded' : 'Running', progressPercent: jobPolls >= 2 ? 100 : 40, errorMessage: null, startedAtUtc: null, completedAtUtc: null })) }); }
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

// ---- change password ----
const pp = await page(student, 'en');
await pp.goto('http://127.0.0.1:4312/app/profile', { waitUntil: 'networkidle0' }); await sleep(1200);
let t = await bodyText(pp);
check('profile shows change-password form', t.includes('Change password'));
await setInput(pp, 'curPw', 'old-pass-1'); await setInput(pp, 'newPw', 'new-pass-123'); await setInput(pp, 'newPw2', 'new-pass-123');
await pp.evaluate(() => { document.querySelector('input[name="curPw"]').closest('form').requestSubmit(); }); await sleep(900);
const cp = find('POST', /^\/me\/change-password$/).at(-1);
check('change password POST {currentPassword,newPassword}', !!cp && cp.body && (() => { const b = JSON.parse(cp.body); return b.currentPassword === 'old-pass-1' && b.newPassword === 'new-pass-123'; })());
check('success message shown', (await bodyText(pp)).includes('Password changed.'));
await pp.screenshot({ path: path.join(out, 'profile-changepw-en.png') }); console.log('shot profile-changepw-en');
await pp.close();

// ---- wallet: course voucher ----
const pw = await page(student, 'en');
await pw.goto('http://127.0.0.1:4312/app/wallet', { waitUntil: 'networkidle0' }); await sleep(1200);
t = await bodyText(pw);
check('wallet shows course voucher box with hint', t.includes('Course access voucher') && t.includes('Wallet credit vouchers'));
await setInput(pw, 'courseCode', 'COURSE-ABC-123');
await pw.evaluate(() => { document.querySelector('input[name="courseCode"]').closest('form').requestSubmit(); }); await sleep(900);
const cv = find('POST', /^\/vouchers\/redeem$/).at(-1);
check('course voucher POST {code}', !!cv && cv.body && JSON.parse(cv.body).code === 'COURSE-ABC-123');
check('redeemed course title shown', (await bodyText(pw)).includes('Algebra II'));
await pw.screenshot({ path: path.join(out, 'wallet-course-voucher-en.png') }); console.log('shot wallet-course-voucher-en');
await pw.close();

// ---- orders: subscriptions cancel ----
const po = await page(student, 'en');
await po.goto('http://127.0.0.1:4312/app/orders', { waitUntil: 'networkidle0' }); await sleep(1200);
t = await bodyText(po);
check('subscriptions listed with plan, price, period', t.includes('Pro Monthly') && t.includes('199 EGP') && t.includes('Monthly') && t.includes('Active'));
await clickText(po, 'ion-button', 'Cancel subscription'); await sleep(400);
t = await bodyText(po);
check('cancel confirm strip shows plan and access-until before sending', t.includes('Confirm cancellation') && t.includes('Pro Monthly') && t.includes('keep access until') && find('POST', /^\/subscriptions\/sub1\/cancel$/).length === 0);
await po.screenshot({ path: path.join(out, 'orders-cancel-confirm-en.png') }); console.log('shot orders-cancel-confirm-en');
await clickText(po, 'ion-button', 'Confirm cancellation'); await sleep(900);
check('cancel POST sent', find('POST', /^\/subscriptions\/sub1\/cancel$/).length === 1);
check('subscriptions refetched after cancel', find('GET', /^\/subscriptions\/mine$/).length >= 2);
await po.close();

// ---- finance: job polling after calculate ----
const pf = await page(admin, 'en');
await pf.goto('http://127.0.0.1:4312/app/finance', { waitUntil: 'networkidle0' }); await sleep(1400);
await clickText(pf, 'button', 'Payouts'); await sleep(900);
await setInput(pf, 'calcFrom', '2026-11-01'); await setInput(pf, 'calcTo', '2026-11-30');
const payoutsBefore = find('GET', /^\/instructor-payouts$/).length;
await pf.evaluate(() => { document.querySelector('input[name="calcFrom"]').closest('form').requestSubmit(); }); await sleep(1500);
t = await bodyText(pf);
check('job progress shown while running', t.includes('Calculation running'));
await sleep(11500);
t = await bodyText(pf);
check('job polled until Succeeded and payouts refreshed', jobPolls >= 2 && t.includes('Payout calculation finished.') && find('GET', /^\/instructor-payouts$/).length === payoutsBefore + 1);
await pf.close();

await browser.close(); server.close();
console.log('\n' + results.join('\n'));
console.log(results.every((r) => r.startsWith('PASS')) ? '\nALL PASS' : '\nSOME FAILED');
