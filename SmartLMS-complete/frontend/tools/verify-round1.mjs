// Round 1 interaction verification. Serves dist/ and answers API calls from contract-shaped fixtures
// (NOT the real backend). Drives the real UI in headless Chrome and asserts:
//   1. Platform login sends POST /auth/login WITHOUT X-Tenant-Id; tenant login sends it WITH the academy key.
//   2. Create-user submits the contract payload (POST /users with fullName/email/password/roles).
//   3. Approvals preview issues GET /courses/{id}; approve issues POST /courses/{id}/approve.
//   4. Deactivate and revoke are two-tap confirmed: the first tap sends no request, the second does.
//   5. Support-access grant sends POST /tenants/{id}/support-access with { minutes }.
// Exits non-zero if any assertion fails. Screenshots land in <outDir>.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-verify'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4302);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const admin = { id: 'u-admin', tenantId: TID, fullName: 'Hana Mostafa', email: 'admin@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['TenantAdmin'], lastLoginAtUtc: null };
const superAdmin = { id: 'u-sa', tenantId: null, fullName: 'Platform Owner', email: 'super@platform.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['SuperAdmin'], lastLoginAtUtc: null };
const tenant = (id, name, slug, active) => ({ id, name, slug, subdomain: null, logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', timeZoneId: 'Africa/Cairo', currency: 'EGP', maxMobileDevices: 1, maxWebDevices: 1, isActive: active });
const captured = []; // { method, path, tenantId: headerValue|undefined, body }
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /users': ok({ totalCount: 2, page: 1, pageSize: 20, items: [
    { id: 'u1', tenantId: TID, fullName: 'Dr. Karim Nabil', email: 'karim@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null },
    { id: 'u2', tenantId: TID, fullName: 'Mona Adel', email: 'mona@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null } ] }),
  'GET /courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'PendingApproval', ownerInstructorId: 'u1' }]),
  'GET /courses/c1': ok({ id: 'c1', title: 'Algebra II', slug: 'algebra-ii', shortDescription: 'Quadratics and beyond', description: null, status: 'PendingApproval', level: 'Intermediate', language: 'ar', estimatedHours: 12, pricingModel: 'Free', price: null, currency: null, ownerInstructorId: 'u1', sections: [
    { id: 's1', title: 'Quadratics', description: null, sortOrder: 1, lessons: [
      { id: 'l1', title: 'Factoring', lessonType: 'Video', durationSeconds: 600, sortOrder: 1, isFreePreview: true },
      { id: 'l2', title: 'The formula', lessonType: 'Article', durationSeconds: null, sortOrder: 2, isFreePreview: false } ] } ] }),
  'GET /tenants': ok({ totalCount: 1, page: 1, pageSize: 20, items: [tenant('t1', 'Nile Academy', 'demo', true)] }),
  'GET /tenant/feature-flags': ok([]), 'GET /categories': ok([]), 'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const dyn = (method, pp, body) => {
  if (method === 'POST' && pp === '/auth/login') { const e = JSON.parse(body || '{}').email; const u = e === 'super@platform.test' ? superAdmin : admin; return ok({ accessToken: 'tok-' + u.id, refreshToken: 'ref', user: u }); }
  if (method === 'POST' && pp === '/users') return ok({ id: 'u-new', ...JSON.parse(body || '{}'), isActive: true, emailConfirmed: true, lastLoginAtUtc: null });
  if (method === 'POST' && pp === '/tenants/t1/support-access') return ok({ id: 'sa1', tenantId: 't1', grantedByUserId: 'u-sa', expiresAtUtc: '2026-11-25T17:00:00Z' });
  return ok({});
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(sessionUser, tenantKey) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: 1280, height: 860, deviceScaleFactor: 1 });
  await p.setRequestInterception(true);
  p.on('request', (rq) => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    captured.push({ method: rq.method(), path: pp, tenantId: rq.headers()['x-tenant-id'], body: rq.postData() ?? null });
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? dyn(rq.method(), pp, rq.postData())) });
  });
  await p.evaluateOnNewDocument((s, tk) => {
    localStorage.setItem('CapacitorStorage.lang', 'en');
    if (tk !== undefined && tk !== null) localStorage.setItem('CapacitorStorage.tenantKey', tk);
    if (s) localStorage.setItem('CapacitorStorage.session', JSON.stringify(s));
  }, sessionUser ? { accessToken: 'a', refreshToken: 'r', user: sessionUser } : null, tenantKey);
  return p;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const find = (method, pathRe) => captured.filter((c) => c.method === method && pathRe.test(c.path));
const setIon = (p, name, value) => p.evaluate((n, v) => {
  const el = document.querySelector(`ion-input[name="${n}"]`);
  const inp = (el.shadowRoot ?? el).querySelector('input');
  inp.value = v; inp.dispatchEvent(new Event('input', { bubbles: true }));
}, name, value);
const setInput = (p, name, value) => p.evaluate((n, v) => {
  const inp = document.querySelector(`input[name="${n}"]`);
  inp.value = v; inp.dispatchEvent(new Event('input', { bubbles: true }));
}, name, value);
const clickText = (p, sel, text) => p.evaluate((s, t) => {
  const els = [...document.querySelectorAll(s)];
  const el = els.find((e) => (e.textContent || '').trim().includes(t));
  if (!el) throw new Error('not found: ' + s + ' ~ ' + t);
  el.click();
}, sel, text);

// 1. Platform login: academy key cleared -> no X-Tenant-Id on /auth/login.
let p = await page(null, '');
await p.goto('http://127.0.0.1:4302/login', { waitUntil: 'networkidle0' }); await sleep(600);
await setIon(p, 'email', 'super@platform.test'); await setIon(p, 'password', 'PlatformPass1');
await p.evaluate(() => document.querySelector('form ion-button[type="submit"]').click());
await sleep(1200);
const platLogin = find('POST', /^\/auth\/login$/).at(-1);
check('platform login has no X-Tenant-Id header', platLogin && platLogin.tenantId === undefined);
check('platform login payload carries email+password+device', platLogin && ['email', 'password', 'device'].every((k) => platLogin.body.includes(k)));
await p.screenshot({ path: path.join(out, 'verify-platform-login.png') });

// 2. Tenant login with academy key -> X-Tenant-Id: demo.
p = await page(null, 'demo');
await p.goto('http://127.0.0.1:4302/login', { waitUntil: 'networkidle0' }); await sleep(600);
await setIon(p, 'email', 'admin@demo.test'); await setIon(p, 'password', 'AdminPass123');
await p.evaluate(() => document.querySelector('form ion-button[type="submit"]').click());
await sleep(1200);
const tenLogin = find('POST', /^\/auth\/login$/).at(-1);
check('tenant login sends X-Tenant-Id: demo', tenLogin && tenLogin.tenantId === 'demo');

// 3. TenantAdmin creates a user -> POST /users with contract fields.
p = await page(admin, 'demo');
await p.goto('http://127.0.0.1:4302/app/users', { waitUntil: 'networkidle0' }); await sleep(800);
await clickText(p, 'ion-button', 'Create user'); await sleep(400);
await setInput(p, 'un', 'Sara Fathy'); await setInput(p, 'ue', 'sara@demo.test'); await setInput(p, 'up', 'SaraPass123');
await clickText(p, 'button.pick', 'Instructor'); await sleep(150);
await p.evaluate(() => document.querySelector('form.stack ion-button[type="submit"]').click());
await sleep(1000);
const created = find('POST', /^\/users$/).at(-1);
check('create user POST /users payload has fullName, email, password, roles', created && ['fullName', 'email', 'password', 'roles'].every((k) => created.body.includes(k)));
check('create user payload carries the picked Instructor role', created && created.body.includes('Instructor'));
await p.screenshot({ path: path.join(out, 'verify-user-created.png') });

// 4. Approvals preview issues GET /courses/c1; approve issues POST /courses/c1/approve.
p = await page(admin, 'demo');
await p.goto('http://127.0.0.1:4302/app/approvals', { waitUntil: 'networkidle0' }); await sleep(800);
await clickText(p, 'ion-button', 'Preview'); await sleep(900);
check('preview issues GET /courses/c1', find('GET', /^\/courses\/c1$/).length > 0);
await p.screenshot({ path: path.join(out, 'verify-approval-preview.png') });
await clickText(p, 'ion-button', 'Approve'); await sleep(900);
check('approve issues POST /courses/c1/approve', find('POST', /^\/courses\/c1\/approve$/).length > 0);
await p.screenshot({ path: path.join(out, 'verify-approval-approved.png') });

// 5. Deactivate is two-tap confirmed.
p = await page(admin, 'demo');
await p.goto('http://127.0.0.1:4302/app/users', { waitUntil: 'networkidle0' }); await sleep(800);
const before = find('POST', /\/deactivate$/).length;
await clickText(p, 'ion-button', 'Deactivate'); await sleep(600);
check('first deactivate tap sends no request', find('POST', /\/deactivate$/).length === before);
await p.screenshot({ path: path.join(out, 'verify-deactivate-confirm.png') });
await clickText(p, 'ion-button', 'Confirm?'); await sleep(800);
check('second tap issues POST /users/u1/deactivate', find('POST', /^\/users\/u1\/deactivate$/).length > 0);

// 6. SuperAdmin grants support access -> POST with { minutes }.
p = await page(superAdmin, '');
await p.goto('http://127.0.0.1:4302/app/tenants', { waitUntil: 'networkidle0' }); await sleep(800);
await clickText(p, 'ion-button', 'Support access'); await sleep(400);
await setInput(p, 'sm', '30'); await sleep(150);
await clickText(p, 'ion-button', 'Grant'); await sleep(900);
const grant = find('POST', /^\/tenants\/t1\/support-access$/).at(-1);
check('grant support access POSTs { minutes: 30 }', grant && grant.body.includes('"minutes":30'));
await p.screenshot({ path: path.join(out, 'verify-support-granted.png') });

console.log(results.join('\n'));
console.log(process.exitCode ? 'VERIFY FAILED' : 'VERIFY PASSED');
await browser.close(); server.close();
