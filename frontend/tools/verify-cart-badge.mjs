// Cart badge verification. Real built UI in headless Chrome against contract fixtures (NOT the real backend).
//  1. Badge shows the API item count on first load (2).
//  2. Removing an item on the cart page updates the badge (1) after the DELETE succeeds.
//  3. A full page reload keeps the badge at 1 (fetched fresh from the API, not local state).
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-cart-badge'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const cart = { items: [{ id: 'ci-1', itemType: 'Course', itemId: 'c-9', description: 'Another course', price: 100, currency: 'EGP', available: true }, { id: 'ci-2', itemType: 'Course', itemId: 'c-10', description: 'Third course', price: 50, currency: 'EGP', available: true }], subtotal: 150, currency: 'EGP' };
const deletes = [];
const posts = [];
const paidCourse = { id: 'c-9', title: 'Paid Course', slug: 'paid-course', shortDescription: null, level: 'Beginner', language: null, estimatedHours: null, pricingModel: 'OneTime', price: 100, currency: 'EGP', description: null, status: 'Published', sections: [] };
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /learning-paths/manage': ok([]), 'GET /learning-paths': ok([]), 'GET /courses/mine': ok([]),
  'GET /courses': ok({ items: [], totalCount: 0 }),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const p = await (await browser.createBrowserContext()).newPage();
await p.setViewport({ width: 1280, height: 800 });
const consoleProblems = [];
p.on('pageerror', (e) => consoleProblems.push('pageerror: ' + e.message));
p.setRequestInterception(true);
p.on('request', (rq) => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  if (rq.method() === 'GET' && pp === '/cart') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ ...cart, subtotal: cart.items.reduce((a, i) => a + i.price, 0) })) });
  if (rq.method() === 'POST' && pp === '/cart/items') { posts.push(rq.postData() ?? ''); cart.items.push({ id: 'ci-3', itemType: 'Course', itemId: 'c-9', description: 'Paid Course', price: 100, currency: 'EGP', available: true }); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({})) }); }
  if (rq.method() === 'POST' && pp === '/orders/checkout') { cart.items = []; return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ orderId: 'o-1', status: 'Paid', subtotal: 150, discountTotal: 0, total: 150, currency: 'EGP', paymentId: null })) }); }
  if (rq.method() === 'GET' && pp === '/courses/c-9') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(paidCourse)) });
  if (rq.method() === 'GET' && pp === '/me/enrollments') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok([])) });
  const del = pp.match(/^\/cart\/items\/(ci-\d)$/);
  if (rq.method() === 'DELETE' && del) { deletes.push(del[1]); cart.items = cart.items.filter((i) => i.id !== del[1]); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({})) }); }
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
});
await p.evaluateOnNewDocument(() => {
  if (localStorage.getItem('CapacitorStorage.seeded')) return;
  localStorage.setItem('CapacitorStorage.seeded', '1');
  localStorage.setItem('CapacitorStorage.lang', 'ar');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: 'u-st', tenantId: 't-1', fullName: 'Student One', email: 'st@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null } }));
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const badge = () => p.evaluate(() => document.querySelector('ion-item .count')?.textContent?.trim() ?? '(hidden)');

await p.goto('http://127.0.0.1:4312/app/courses', { waitUntil: 'networkidle0' }); await sleep(1500);
check('badge shows API count (2) on load', (await badge()) === '2');

await p.goto('http://127.0.0.1:4312/app/cart', { waitUntil: 'networkidle0' }); await sleep(1500);
check('badge still 2 on the cart page', (await badge()) === '2');
await p.evaluate(() => { const b = [...document.querySelectorAll('.it ion-button')].pop(); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(1500);
check('remove hit DELETE /cart/items/ci-2', deletes.length === 1 && deletes[0] === 'ci-2');
check('badge updated to 1 after remove', (await badge()) === '1');

await p.reload({ waitUntil: 'networkidle0' }); await sleep(1500);
check('badge stays 1 after a full reload (from the API)', (await badge()) === '1');
await p.screenshot({ path: path.join(out, 'cart-badge-ar.png') });

// add path: adding a paid course bumps the badge
await p.goto('http://127.0.0.1:4312/app/courses/c-9', { waitUntil: 'networkidle0' }); await sleep(1500);
await p.evaluate(() => { const b = [...document.querySelectorAll('ion-button')].find((x) => (x.textContent || '').includes('أضف للسلة')); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(1500);
check('add-to-cart hit POST /cart/items for the course', posts.length === 1 && /c-9/.test(posts[0]));
check('badge bumped to 2 after add', (await badge()) === '2');

// checkout path: a paid order empties the cart and the badge disappears
await p.goto('http://127.0.0.1:4312/app/cart', { waitUntil: 'networkidle0' }); await sleep(1500);
await p.evaluate(() => { const b = [...document.querySelectorAll('ion-button')].find((x) => (x.textContent || '').includes('إتمام الشراء')); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(1800);
check('badge clears after a successful checkout', (await badge()) === '(hidden)');

// role switch: a non-Student session never renders the badge
const p2 = await (await browser.createBrowserContext()).newPage();
await p2.setViewport({ width: 1280, height: 800 });
p2.setRequestInterception(true);
p2.on('request', (rq) => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
});
await p2.evaluateOnNewDocument(() => {
  localStorage.setItem('CapacitorStorage.seeded', '1');
  localStorage.setItem('CapacitorStorage.lang', 'ar');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: 'u-inst', tenantId: 't-1', fullName: 'Instructor One', email: 'inst@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null } }));
});
await p2.goto('http://127.0.0.1:4312/app/courses', { waitUntil: 'networkidle0' }); await sleep(1500);
check('no badge in a non-Student session', (await p2.evaluate(() => document.querySelector('.count')?.textContent ?? '(hidden)')) === '(hidden)');
await p2.close();

check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
