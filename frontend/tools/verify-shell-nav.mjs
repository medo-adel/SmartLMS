// Shell navigation verification. Real UI in headless Chrome against contract fixtures (NOT the real backend).
// Asserts the sidebar fix at the user's reported viewport (1280x604, Arabic, long Instructor+Student menu):
//  1. EVERY menu item is reachable by scrolling the list; the last item is never hidden behind the account footer.
//  2. The account footer stays pinned at the bottom, in flow (no overlap).
//  3. No missing-ionicons warnings and no "Invalid base URL" errors in the console.
//  4. Register page shows the explicit account-type choice (Student default, Parent option).
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-shell-nav'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const multiRole = { id: 'u-inst', tenantId: 't-1', fullName: 'Instructor One', email: 'inst@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student', 'Instructor'], lastLoginAtUtc: null };
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /learning-paths/manage': ok([]), 'GET /learning-paths': ok([]), 'GET /courses/mine': ok([]),
  'GET /courses': ok({ items: [], totalCount: 0 }),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const p = await (await browser.createBrowserContext()).newPage();
await p.setViewport({ width: 1280, height: 604 }); // the user's reported viewport
const consoleProblems = [];
p.on('console', (m) => { const t = m.text(); if (/Ionicons Warning|Invalid base URL/i.test(t)) consoleProblems.push(t); });
p.on('pageerror', (e) => consoleProblems.push('pageerror: ' + e.message));
p.setRequestInterception(true);
p.on('request', (rq) => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
});
await p.evaluateOnNewDocument(() => {
  if (localStorage.getItem('CapacitorStorage.seeded')) return;
  localStorage.setItem('CapacitorStorage.seeded', '1');
  localStorage.setItem('CapacitorStorage.lang', 'ar');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: 'u-inst', tenantId: 't-1', fullName: 'Instructor One', email: 'inst@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student', 'Instructor'], lastLoginAtUtc: null } }));
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };

await p.goto('http://127.0.0.1:4312/app/paths', { waitUntil: 'networkidle0' }); await sleep(1200);

const nav = await p.evaluate(() => {
  const list = document.querySelector('ion-menu ion-list');
  const who = document.querySelector('ion-menu .who');
  const items = [...document.querySelectorAll('ion-menu ion-list ion-item')];
  const before = items.map((el, i) => ({ i, text: (el.textContent || '').trim().slice(0, 12), top: el.getBoundingClientRect().top, bottom: el.getBoundingClientRect().bottom }));
  list.scrollTop = list.scrollHeight; // scroll to the very end
  const whoR = who.getBoundingClientRect();
  const last = items[items.length - 1].getBoundingClientRect();
  return {
    count: items.length,
    scrollable: list.scrollHeight > list.clientHeight,
    before,
    lastBottom: last.bottom, whoTop: whoR.top,
    footerVisible: whoR.bottom <= window.innerHeight && whoR.bottom > 0,
    iconsMissing: [...document.querySelectorAll('ion-menu ion-icon')].filter((el) => !el.shadowRoot || !el.shadowRoot.querySelector('svg')).map((el) => el.getAttribute('name')),
  };
});
await sleep(400);
await p.screenshot({ path: path.join(out, 'shell-nav-ar-scrolled.png') }); console.log('shot shell-nav-ar-scrolled');
check('long menu (>= 12 items) renders for multi-role user', nav.count >= 12);
check('paths page loads without a generic error (fixture routes match real API paths)', await p.evaluate(() => !document.body.textContent.includes('حاول مرة أخرى') && !document.body.textContent.includes('حدث خطأ')));
check('menu list is scrollable at 604px viewport', nav.scrollable);
check('after scrolling, last menu item clears the account footer', nav.lastBottom <= nav.whoTop + 1);
check('account footer pinned and visible at the bottom', nav.footerVisible);
check('every menu icon renders real SVG (trophy/cash/shield registered)', nav.iconsMissing.length === 0);
if (nav.iconsMissing.length) console.log('missing icons:', nav.iconsMissing.join(','));
check('no ionicons warnings / Invalid base URL errors in console', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.slice(0, 4).join('\n'));

// Register page: explicit account-type choice.
const p2 = await (await browser.createBrowserContext()).newPage();
await p2.setViewport({ width: 1280, height: 800 });
p2.setRequestInterception(true);
p2.on('request', (rq) => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(fx['GET ' + new URL(rq.url()).pathname.replace('/api/v1', '')] ?? ok({ id: 'x' })) });
});
await p2.evaluateOnNewDocument(() => { if (!localStorage.getItem('CapacitorStorage.seeded')) { localStorage.setItem('CapacitorStorage.seeded', '1'); localStorage.setItem('CapacitorStorage.lang', 'ar'); } });
await p2.goto('http://127.0.0.1:4312/register', { waitUntil: 'networkidle0' }); await sleep(900);
const reg = await p2.evaluate(() => {
  const sel = document.querySelector('select#regRole');
  return { hasSelect: !!sel, options: sel ? [...sel.options].map((o) => o.value) : [], value: sel?.value, hint: document.body.textContent.includes('التسجيل الذاتي ينشئ') };
});
await p2.screenshot({ path: path.join(out, 'register-role-ar.png') }); console.log('shot register-role-ar');
check('register page shows the account-type select', reg.hasSelect);
check('register roles are exactly Student+Parent, Student default', reg.options.join(',') === 'Student,Parent' && reg.value === 'Student');
check('register page explains staff accounts come from the academy', reg.hint);

// Truthful code-step copy: with a local-outbox backend the UI must NOT promise an inbox.
await p2.evaluate(() => {
  const set = (name, v) => { const el = document.querySelector(`ion-input[name="${name}"] input`); el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); };
  set('fullName', 'Flow Student'); set('academy', 'demo'); set('email', 'flow@demo.test'); set('password', 'Str0ngPassw0rd');
});
await p2.evaluate(() => { [...document.querySelectorAll('ion-button')].find((b) => b.type === 'submit').click(); });
await sleep(1200);
const sent = await p2.evaluate(() => (document.querySelector('.ok')?.textContent ?? '').trim());
await p2.screenshot({ path: path.join(out, 'register-code-step-ar.png') }); console.log('shot register-code-step-ar');
check('local-outbox mode: copy points at console/dev-mail, not the inbox', sent.includes('dev-mail') && !sent.includes('بريدك الإلكتروني'));

// Discoverability: a staff member must find course creation from the catalog even when it is empty.
await p.goto('http://127.0.0.1:4312/app/courses', { waitUntil: 'networkidle0' }); await sleep(1200);
const cta = await p.evaluate(() => { const a = [...document.querySelectorAll('ion-button, a')].filter((b) => (b.getAttribute('href') ?? '').includes('/app/build')); return a.length; });
check('empty catalog shows a create/manage-courses CTA for staff (multi-role session)', cta >= 2); // header + empty state
await p.screenshot({ path: path.join(out, 'catalog-staff-cta-ar.png') }); console.log('shot catalog-staff-cta-ar');
await p.goto('http://127.0.0.1:4312/app/build', { waitUntil: 'networkidle0' }); await sleep(1200);
const buildOk = await p.evaluate(() => document.querySelectorAll('input, ion-input').length > 0 && !document.querySelector('.err'));
check('builder page renders for the instructor (create-course form reachable)', buildOk);
await p.screenshot({ path: path.join(out, 'builder-create-ar.png') }); console.log('shot builder-create-ar');

console.log(results.join('\n'));
await browser.close(); server.close();
console.log(process.exitCode ? 'FAILURES PRESENT' : 'ALL PASS ' + results.length + '/' + results.length);
