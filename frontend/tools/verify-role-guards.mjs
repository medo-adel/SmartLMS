// Role-visibility verification. Real built UI in headless Chrome, fixture HTTP (NOT the real backend).
// Asserts shell tabs match what the backend allows per role, and no visible page makes a forbidden call:
//  - Instructor on /app/orders: no GET /orders (Student-only on the API), affiliate panel shows, no error.
//  - Parent on /app/community: announcements/rooms load, GET /me/enrollments is never called (Student-only).
//  - Shell: Instructor sees paths; TenantAdmin sees paths + assessments; Parent sees neither paths nor achievements;
//    Student sees both. Direct navigation to /app/paths as Parent is redirected by the route guard.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); fs.mkdirSync(process.argv[2] ?? 'shots-role-guards', { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const hits = { orders: 0, enrollments: 0 };
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /courses': ok({ items: [], totalCount: 0 }),
  'GET /affiliate/me': ok({ referralCode: 'REF-1', commissionPercent: 10, totalCredited: 0, commissions: [], referrals: [] }),
  'GET /announcements': ok([]),
  'GET /chat/rooms': ok([]),
  'GET /learning-paths': ok([]),
  'GET /parent/children': ok([]),
  'GET /parent/alerts': ok([]),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const consoleProblems = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };

async function session(roles, id) {
  const ctx = await browser.createBrowserContext();
  const p = await ctx.newPage();
  await p.setViewport({ width: 1280, height: 800 });
  p.on('pageerror', (e) => consoleProblems.push(id + ' pageerror: ' + e.message));
  p.setRequestInterception(true);
  p.on('request', (rq) => { void (async () => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (rq.method() === 'GET' && pp === '/orders') hits.orders++;
    if (rq.method() === 'GET' && pp === '/me/enrollments') hits.enrollments++;
    const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
  })().catch(() => { try { rq.abort(); } catch { /* already handled */ } });
  });
  await p.evaluateOnNewDocument((rolesJson, uid) => {
    if (localStorage.getItem('CapacitorStorage.seeded')) return;
    localStorage.setItem('CapacitorStorage.seeded', '1');
    localStorage.setItem('CapacitorStorage.lang', 'ar');
    localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
    localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: uid, tenantId: 't-1', fullName: 'User One', email: 'u@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: JSON.parse(rolesJson), lastLoginAtUtc: null } }));
  }, JSON.stringify(roles), id);
  return p;
}
const links = (p) => p.evaluate(() => [...document.querySelectorAll('ion-menu ion-item[href]')].map((e) => e.getAttribute('href')));

// 1. Instructor: orders page without the Student-only orders call
const inst = await session(['Instructor'], 'u-inst');
await inst.goto('http://127.0.0.1:4312/app/orders', { waitUntil: 'networkidle0' }); await sleep(1500);
const instState = await inst.evaluate(() => ({ err: [...document.querySelectorAll('p.err')].length, aff: (document.body.textContent || '').includes('REF-1') }));
check('instructor orders page: no forbidden error, affiliate panel shown', instState.err === 0 && instState.aff);
check('instructor never calls Student-only GET /orders', hits.orders === 0);
const instLinks = await links(inst);
check('instructor shell shows paths (manage supported by API)', instLinks.includes('/app/paths'));
check('instructor shell keeps assessments', instLinks.includes('/app/assessments'));
await inst.close();

// 2. TenantAdmin: paths + assessments visible (both allowed by the API for this role)
const ta = await session(['TenantAdmin'], 'u-ta');
await ta.goto('http://127.0.0.1:4312/app/dashboard', { waitUntil: 'networkidle0' }); await sleep(1200);
const taLinks = await links(ta);
check('tenant-admin shell shows paths (manage supported by API)', taLinks.includes('/app/paths'));
check('tenant-admin shell shows assessments (authoring allowed by API)', taLinks.includes('/app/assessments'));
await ta.close();

// 3. Parent: no paths/achievements tabs; community loads without the Student-only enrollments call
const par = await session(['Parent'], 'u-par');
await par.goto('http://127.0.0.1:4312/app/dashboard', { waitUntil: 'networkidle0' }); await sleep(1200);
const parLinks = await links(par);
check('parent shell hides paths and achievements', !parLinks.includes('/app/paths') && !parLinks.includes('/app/achievements'));
await par.goto('http://127.0.0.1:4312/app/community', { waitUntil: 'networkidle0' }); await sleep(1500);
const parComm = await par.evaluate(() => [...document.querySelectorAll('p.err')].length);
check('parent community page loads without forbidden error', parComm === 0);
check('parent never calls Student-only GET /me/enrollments', hits.enrollments === 0);
await par.goto('http://127.0.0.1:4312/app/paths', { waitUntil: 'networkidle0' }); await sleep(1200);
check('direct /app/paths as parent redirects away (route guard)', await par.evaluate(() => location.pathname) !== '/app/paths');
await par.close();

// 4. Student: sees both tabs
const st = await session(['Student'], 'u-st');
await st.goto('http://127.0.0.1:4312/app/dashboard', { waitUntil: 'networkidle0' }); await sleep(1200);
const stLinks = await links(st);
check('student shell shows paths and achievements', stLinks.includes('/app/paths') && stLinks.includes('/app/achievements'));
await st.close();

check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
