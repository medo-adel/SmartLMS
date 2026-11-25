// Submit-for-approval feedback verification. Real built UI in headless Chrome, fixture HTTP (NOT the real backend).
// Asserts: the button disables while submitting (repeat clicks can't double-submit), a success toast appears
// ONLY after the server accepts, the course list is refetched (status now pending), and a failure shows the
// server's error with no success toast.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-builder-submit'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });

const lesson = { id: 'les-1', title: 'Lesson One', lessonType: 'Video', durationSeconds: null, sortOrder: 0, isFreePreview: false };
const mkCourse = (status) => ({ id: 'c-1', title: 'Test Course', slug: 'test-course', shortDescription: null, level: 'Beginner', language: null, estimatedHours: null, pricingModel: 'Free', price: 0, currency: 'EGP', description: null, status, sections: [{ id: 'sec-1', title: 'Section One', description: null, sortOrder: 0, lessons: [lesson] }] });
let status = 'Draft';
let failSubmit = false;
const submits = []; let mineGets = 0;
const fx = () => ({
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /courses': ok({ items: [], totalCount: 0 }),
  'GET /courses/mine': ok([{ id: 'c-1', title: 'Test Course', status, ownerInstructorId: 'u-inst' }]),
  'GET /courses/c-1': ok(mkCourse(status)),
  'GET /courses/c-1/staff': ok([]), 'GET /courses/c-1/prerequisites': ok([]),
  'GET /lessons/les-1/resources': ok([]),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
});
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const ctx = await browser.createBrowserContext();
const p = await ctx.newPage();
await p.setViewport({ width: 1280, height: 800 });
const consoleProblems = [];
p.on('pageerror', (e) => consoleProblems.push('pageerror: ' + e.message));
p.setRequestInterception(true);
p.on('request', (rq) => { void (async () => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  if (rq.method() === 'POST' && pp === '/courses/c-1/submit-for-approval') {
    submits.push(Date.now());
    if (failSubmit) return rq.respond({ status: 400, headers: h, body: JSON.stringify({ success: false, data: null, message: 'Course must have at least one published lesson', errors: [] }) });
    status = 'PendingApproval';
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({})) });
  }
  if (rq.method() === 'GET' && pp === '/courses/mine') mineGets++;
  const f = fx()[rq.method() + ' ' + pp] ?? fx()['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
})().catch((e) => { consoleProblems.push('handler: ' + e.message); try { rq.abort(); } catch { /* already handled */ } });
});
await p.evaluateOnNewDocument(() => {
  if (localStorage.getItem('CapacitorStorage.seeded')) return;
  localStorage.setItem('CapacitorStorage.seeded', '1');
  localStorage.setItem('CapacitorStorage.lang', 'ar');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: 'u-inst', tenantId: 't-1', fullName: 'Instructor One', email: 'inst@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null } }));
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };

await p.goto('http://127.0.0.1:4312/app/build', { waitUntil: 'networkidle0' }); await sleep(1200);
await p.evaluate(() => { const el = [...document.querySelectorAll('.tabs2 button')].find((x) => (x.textContent || '').includes('Test Course')); el?.click(); });
await sleep(1500);
const mineBefore = mineGets;

// rapid double click: the in-flight guard must allow exactly one request
await p.evaluate(() => { const b = [...document.querySelectorAll('ion-button')].find((x) => (x.textContent || '').includes('أرسل للموافقة')); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
const toastTxt = () => [...document.querySelectorAll('ion-toast')].map((t) => ((t.shadowRoot?.textContent || '') + (t.textContent || '')).trim()).join(' | ');
await p.waitForFunction(() => [...document.querySelectorAll('ion-toast')].some((t) => ((t.shadowRoot?.textContent || '') + (t.textContent || '')).includes('تم إرسال الكورس للمراجعة')), { timeout: 8000 }).catch(() => {});
await sleep(600);
const toastText = await p.evaluate(toastTxt);
check('exactly one submit request despite the double click', submits.length === 1);
check('success toast after the server accepted', toastText.includes('تم إرسال الكورس للمراجعة'));
check('course list refetched after success (status now pending)', mineGets === mineBefore + 1);
await p.screenshot({ path: path.join(out, 'builder-submit-toast-ar.png') });

// failure: server rejects -> visible error, NO success toast. Wait for the first toast to dismiss so only new toasts count.
await p.waitForFunction(() => ![...document.querySelectorAll('ion-toast')].some((t) => ((t.shadowRoot?.textContent || '') + (t.textContent || '')).includes('تم إرسال الكورس للمراجعة')), { timeout: 10000 }).catch(() => {});
failSubmit = true;
await p.evaluate(() => { const b = [...document.querySelectorAll('ion-button')].find((x) => (x.textContent || '').includes('أرسل للموافقة')); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await p.waitForFunction(() => (document.querySelector('p.err')?.textContent || '').length > 0, { timeout: 8000 }).catch(() => {});
await sleep(400);
const failState = await p.evaluate(() => ({ err: document.querySelector('p.err')?.textContent.trim() ?? '', toasts: [...document.querySelectorAll('ion-toast')].map((t) => ((t.shadowRoot?.textContent || '') + (t.textContent || '')).trim()).join(' | ') }));
check('failed submit hits the server and shows its error', submits.length === 2 && /published lesson/.test(failState.err));
check('no premature success toast on failure', !failState.toasts.includes('تم إرسال الكورس للمراجعة'));
check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
