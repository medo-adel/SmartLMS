// Platform-context verification. Real UI in headless Chrome against contract fixtures (NOT the real backend).
// Asserts the SuperAdmin academy-context fix:
//  1. Platform context (no tenant key): /app/ops and /app/community fire ZERO tenant calls and show the select-academy notice.
//  2. Academies page: "Enter context" on an academy row POSTs a time-boxed support-access grant for THAT academy, then tenant calls carry its key.
//  3. In academy context, SuperAdmin sees only the audit tab (bin/keys/hooks are TenantAdmin-only in the backend).
//  4. "Exit academy context" clears the tenant key back to platform context.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-platform-context'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4311);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const superAdmin = { id: 'u-sa', tenantId: null, fullName: 'Platform Admin', email: 'root@platform.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['SuperAdmin'], lastLoginAtUtc: null };
const tenant = { id: 't-1', name: 'Nile Academy', slug: 'demo', subdomain: null, logoUrl: null, primaryColor: null, secondaryColor: null, accentColor: null, defaultLanguage: 'en', timeZoneId: 'Africa/Cairo', currency: 'EGP', maxMobileDevices: 1, maxWebDevices: 1, isActive: true };
const auditRow = { id: 'a1', userId: 'u-admin', userName: 'Hana Mostafa', action: 'Create', entityName: 'Course', entityId: 'c1', oldValuesJson: null, newValuesJson: null, ipAddress: '127.0.0.1', correlationId: 'x', occurredAtUtc: '2026-11-25T10:00:00Z' };
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /tenants': ok({ items: [tenant], page: 1, pageSize: 20, totalCount: 1 }),
  'POST /tenants/t-1/support-access': ok({ tenantId: 't-1', expiresAtUtc: '2026-11-25T22:00:00Z' }),
  'GET /audit-logs': ok({ items: [auditRow], page: 1, pageSize: 20, totalCount: 1 }),
  'POST /auth/login': ok({ accessToken: 'sa', refreshToken: 'sr', user: superAdmin }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const dyn = () => ok({ id: 'new-id' });
// Per-run failure modes: path -> [status, body]. Used for the expired-grant and login-retry scenarios.
let failModes = {};
const fail = (code, message) => ({ success: false, data: null, message, code, errors: [] });
const TID2 = '22222222-2222-2222-2222-222222222222';
const tenantAdmin = { id: 'u-admin', tenantId: TID2, fullName: 'Hana Mostafa', email: 'admin@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['TenantAdmin'], lastLoginAtUtc: null };
const captured = [];
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(tenantKey, sessionUser = superAdmin) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: 1280, height: 900 });
  await p.setRequestInterception(true);
  p.on('request', (rq) => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    captured.push({ method: rq.method(), path: pp, tenant: rq.headers()['x-tenant-id'] ?? null, body: rq.postData() ?? null });
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    const fm = failModes[pp];
    if (fm && (fm.when ? fm.when(rq) : true)) return rq.respond({ status: fm.status, headers: h, body: JSON.stringify(fm.body) });
    const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? dyn()) });
  });
  await p.evaluateOnNewDocument((tk, u) => {
    if (localStorage.getItem('CapacitorStorage.session') || localStorage.getItem('CapacitorStorage.seeded')) return; // seed once; later navigations keep real storage behavior
    localStorage.setItem('CapacitorStorage.seeded', '1');
    localStorage.setItem('CapacitorStorage.lang', 'en');
    localStorage.setItem('CapacitorStorage.tenantKey', tk);
    if (u) localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
  }, tenantKey, sessionUser);
  return p;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const find = (method, pathRe) => captured.filter((c) => c.method === method && pathRe.test(c.path));
const clickText = (p, sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim().includes(t)); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);

// 1. Platform context: tenant pages show the notice and fire zero tenant calls.
const p = await page('');
await p.goto('http://127.0.0.1:4311/app/ops', { waitUntil: 'networkidle0' }); await sleep(900);
check('platform context: ops fires no audit request', find('GET', /^\/audit-logs/).length === 0);
check('platform context: ops shows select-academy notice', await p.evaluate(() => document.body.textContent.includes('Select an academy')));
check('platform context: ops hides audit/bin/keys/hooks tabs', await p.evaluate(() => ![...document.querySelectorAll('.tabs2 button')].length));
await p.screenshot({ path: path.join(out, 'ops-platform-context-en.png') }); console.log('shot ops-platform-context-en');
await p.goto('http://127.0.0.1:4311/app/community', { waitUntil: 'networkidle0' }); await sleep(900);
check('platform context: community fires no announcements request', find('GET', /^\/announcements/).length === 0);
check('platform context: community shows select-academy notice', await p.evaluate(() => document.body.textContent.includes('Select an academy')));
await p.screenshot({ path: path.join(out, 'community-platform-context-en.png') }); console.log('shot community-platform-context-en');

// 2. Enter academy context from the academies page.
await p.goto('http://127.0.0.1:4311/app/tenants', { waitUntil: 'networkidle0' }); await sleep(900);
check('academies list loads (platform route)', find('GET', /^\/tenants(\?|$)/).length > 0);
await clickText(p, 'ion-button', 'Enter context'); await sleep(900);
check('enter context POSTs support-access grant for the shown academy', find('POST', /^\/tenants\/t-1\/support-access$/).length === 1);
check('enter context sets the tenant key', await p.evaluate(() => localStorage.getItem('CapacitorStorage.tenantKey')) === 'demo');

// 3. In academy context: audit works and carries the tenant key; TenantAdmin-only tabs stay hidden for SuperAdmin.
await p.goto('http://127.0.0.1:4311/app/ops', { waitUntil: 'networkidle0' }); await sleep(900);
const auditCalls = find('GET', /^\/audit-logs/);
check('academy context: audit request fired', auditCalls.length > 0);
check('academy context: audit request carries X-Tenant-Id=demo', auditCalls.some((c) => c.tenant === 'demo'));
check('academy context: audit row rendered', await p.evaluate(() => document.body.textContent.includes('Hana Mostafa')));
check('academy context: bin/keys/hooks tabs hidden for SuperAdmin', await p.evaluate(() => document.querySelectorAll('.tabs2 button').length === 1));
check('academy context: no recycle-bin/keys/webhooks requests', find('GET', /^\/(recycle-bin|api-keys|webhooks)/).length === 0);
await p.screenshot({ path: path.join(out, 'ops-academy-context-en.png') }); console.log('shot ops-academy-context-en');

// 4. Exit back to platform context.
await p.goto('http://127.0.0.1:4311/app/tenants', { waitUntil: 'networkidle0' }); await sleep(900);
await clickText(p, 'ion-button', 'Exit academy context'); await sleep(700);
check('exit context clears the tenant key', await p.evaluate(() => localStorage.getItem('CapacitorStorage.tenantKey')) === '');
await p.goto('http://127.0.0.1:4311/app/ops', { waitUntil: 'networkidle0' }); await sleep(900);
check('after exit: ops shows the notice again', await p.evaluate(() => document.body.textContent.includes('Select an academy')));
check('after exit: no new audit request', find('GET', /^\/audit-logs/).length === 1);

// 5. Expired/revoked support grant: backend 403 SUPPORT_ACCESS_REQUIRED -> visible way back to academy selection, no bypass.
captured.length = 0;
failModes = { '/audit-logs': { status: 403, body: fail('SUPPORT_ACCESS_REQUIRED', 'SuperAdmin needs an active support-access grant for this tenant.') },
  '/announcements': { status: 403, body: fail('SUPPORT_ACCESS_REQUIRED', 'SuperAdmin needs an active support-access grant for this tenant.') } };
const p2 = await page('demo');
await p2.goto('http://127.0.0.1:4311/app/ops', { waitUntil: 'networkidle0' }); await sleep(900);
check('expired grant: audit request attempted once with the tenant key', find('GET', /^\/audit-logs/).filter((c) => c.tenant === 'demo').length === 1);
check('expired grant: ops shows the academy-selection notice', await p2.evaluate(() => document.body.textContent.includes('Select an academy')));
check('expired grant: no audit retry loop', find('GET', /^\/audit-logs/).length === 1);
await p2.screenshot({ path: path.join(out, 'ops-grant-expired-en.png') }); console.log('shot ops-grant-expired-en');
await p2.goto('http://127.0.0.1:4311/app/community', { waitUntil: 'networkidle0' }); await sleep(900);
check('expired grant: community shows the academy-selection notice', await p2.evaluate(() => document.body.textContent.includes('Select an academy')));
failModes = {};


// 6. Platform login after academy context: first attempt carries the stale key, retry omits it, session lands, key reset.
captured.length = 0;
const p3 = await page('demo', null);
failModes = { '/auth/login': { status: 401, body: fail('INVALID_CREDENTIALS', 'Invalid email or password.'), when: (rq) => !!rq.headers()['x-tenant-id'] } };
await p3.goto('http://127.0.0.1:4311/login', { waitUntil: 'networkidle0' }); await sleep(700);
await p3.evaluate(() => { const e = document.querySelector('ion-input[name="email"] input'); e.value = 'root@platform.test'; e.dispatchEvent(new Event('input', { bubbles: true })); });
await p3.evaluate(() => { const e = document.querySelector('ion-input[name="password"] input'); e.value = 'super-secret-pass'; e.dispatchEvent(new Event('input', { bubbles: true })); });
await p3.evaluate(() => { document.querySelector('form').requestSubmit(); }); await sleep(1200);
const logins = find('POST', /^\/auth\/login$/);
check('platform login: first attempt carried the stale academy key', logins.length >= 1 && logins[0].tenant === 'demo');
check('platform login: retried once without the tenant header', logins.length === 2 && logins[1].tenant === null);
check('platform login: session stored after retry', await p3.evaluate(() => !!localStorage.getItem('CapacitorStorage.session')));
check('platform login: tenant key reset to platform context', (await p3.evaluate(() => localStorage.getItem('CapacitorStorage.tenantKey'))) === '');
failModes = {};


// 7. Ordinary tenant login: single attempt WITH the academy key, no retry, academy preserved.
captured.length = 0;
const p4 = await page('demo', null);
failModes = { '/auth/login': { status: 200, body: ok({ accessToken: 'ta', refreshToken: 'tr', user: tenantAdmin }) } };
await p4.goto('http://127.0.0.1:4311/login', { waitUntil: 'networkidle0' }); await sleep(700);
await p4.evaluate(() => { const e = document.querySelector('ion-input[name="email"] input'); e.value = 'admin@demo.test'; e.dispatchEvent(new Event('input', { bubbles: true })); });
await p4.evaluate(() => { const e = document.querySelector('ion-input[name="password"] input'); e.value = 'correct-tenant-pass'; e.dispatchEvent(new Event('input', { bubbles: true })); });
await p4.evaluate(() => { document.querySelector('form').requestSubmit(); }); await sleep(1200);
const tLogins = find('POST', /^\/auth\/login$/).filter((c) => c.body && c.body.includes('admin@demo.test'));
check('tenant login: exactly one attempt, with the academy key', tLogins.length === 1 && tLogins[0].tenant === 'demo');
check('tenant login: academy adopted from the token', (await p4.evaluate(() => localStorage.getItem('CapacitorStorage.tenantKey'))) === TID2);
failModes = {};


console.log('\n' + results.join('\n'));
console.log(results.every((r) => r.startsWith('PASS')) ? 'ALL PASS ' + results.length + '/' + results.length : 'SOME FAILED');
await browser.close(); server.close();
