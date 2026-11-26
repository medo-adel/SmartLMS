// Wishlist: heart toggle on course-detail + saved list page. Real built UI in headless Chrome, fixture HTTP
// (NOT the real backend). Asserts: the heart reflects the server-side wishlist state, toggling sends the right
// POST/DELETE, the wishlist page renders exactly the server's rows (Arabic RTL, phone width), and
// "move to cart" adds to the cart AND removes from the wishlist.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-wishlist'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });

const course = { id: 'c-1', title: 'أساسيات الجبر', slug: 'algebra', shortDescription: 'من الصفر للاحتراف', level: 'Beginner', language: 'ar', estimatedHours: 6, pricingModel: 'OneTime', price: 250, currency: 'EGP', description: 'كورس جبر كامل للمرحلة الثانوية.', status: 'Published', sections: [{ id: 'sec-1', title: 'القسم الأول', description: null, sortOrder: 0, lessons: [{ id: 'les-1', title: 'مقدمة', lessonType: 'Video', durationSeconds: 300, sortOrder: 0, isFreePreview: true }] }] };
let wishlist = [
  { id: 'w-1', courseId: 'c-1', title: 'أساسيات الجبر', slug: 'algebra', shortDescription: 'من الصفر للاحتراف', thumbnailFileId: null, pricingModel: 'OneTime', price: 250, currency: 'EGP', createdAtUtc: '2026-11-20T10:00:00Z' },
  { id: 'w-2', courseId: 'c-2', title: 'فيزياء أولى ثانوي', slug: 'physics', shortDescription: null, thumbnailFileId: null, pricingModel: 'Free', price: 0, currency: 'EGP', createdAtUtc: '2026-11-21T10:00:00Z' },
];
const calls = []; // captured wishlist/cart writes { method, path, body }
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /courses/c-1': ok(course),
  'GET /me/enrollments': ok([]),
  'GET /courses/c-1/exams': ok([]),
  'GET /cart': ok({ items: [], subtotal: 0, currency: 'EGP' }),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const p = await (await browser.createBrowserContext()).newPage();
await p.setViewport({ width: 1280, height: 800 });
const consoleProblems = [];
p.on('pageerror', (e) => consoleProblems.push('pageerror: ' + e.message));
p.setRequestInterception(true);
p.on('request', (rq) => { void (async () => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  if (pp === '/wishlist' && rq.method() === 'GET') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ items: wishlist })) });
  const wm = pp.match(/^\/wishlist\/(c-\d)$/);
  if (wm && rq.method() === 'POST') { calls.push({ method: 'POST', path: pp, body: rq.postData() ?? '' }); if (!wishlist.some((i) => i.courseId === wm[1])) wishlist.push({ id: 'w-new', courseId: wm[1], title: course.title, slug: course.slug, shortDescription: course.shortDescription, thumbnailFileId: null, pricingModel: course.pricingModel, price: course.price, currency: course.currency, createdAtUtc: '2026-11-26T02:00:00Z' }); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ id: 'w-new' })) }); }
  if (wm && rq.method() === 'DELETE') { calls.push({ method: 'DELETE', path: pp, body: '' }); wishlist = wishlist.filter((i) => i.courseId !== wm[1]); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(null)) }); }
  if (pp === '/cart/items' && rq.method() === 'POST') { calls.push({ method: 'POST', path: pp, body: rq.postData() ?? '' }); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ id: 'cart-1' })) }); }
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({})) });
})().catch((e) => { consoleProblems.push('fixture: ' + e.message); rq.abort(); }); });
await p.evaluateOnNewDocument(() => {
  localStorage.setItem('CapacitorStorage.lang', 'ar');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: 'u-st', tenantId: 't-1', fullName: 'طالب تجريبي', email: 'st@demo.test', roles: ['Student'] } }));
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };

// course-detail: c-1 starts in the wishlist, so the heart is filled
await p.goto('http://127.0.0.1:4312/app/courses/c-1', { waitUntil: 'networkidle0' }); await sleep(1500);
const heartOn = await p.evaluate(() => { const b = document.querySelector('app-course-detail .heart.on'); return !!b && b.textContent.trim() === '♥'; });
check('detail: filled heart for a wishlisted course', heartOn);
await p.evaluate(() => { document.querySelector('app-course-detail .heart').dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(800);
const heartOff = await p.evaluate(() => { const b = document.querySelector('app-course-detail .heart'); return !!b && !b.classList.contains('on') && b.textContent.trim() === '♡'; });
check('detail: toggle sends DELETE and empties the heart', heartOff && calls.some((c) => c.method === 'DELETE' && c.path === '/wishlist/c-1'));
await p.evaluate(() => { document.querySelector('app-course-detail .heart').dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(800);
check('detail: second toggle sends POST and refills the heart', calls.some((c) => c.method === 'POST' && c.path === '/wishlist/c-1'));

// wishlist page: exactly the server's rows, Arabic RTL at phone width
await p.goto('http://127.0.0.1:4312/app/wishlist', { waitUntil: 'networkidle0' }); await sleep(1500);
await p.setViewport({ width: 390, height: 844 }); await sleep(900);
const list = await p.evaluate(() => { const pg = [...document.querySelectorAll('app-wishlist')].find((e) => e.offsetParent !== null); if (!pg) return null; return { rows: [...pg.querySelectorAll('[data-testid="wishlist-item"]')].map((r) => r.textContent), overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth }; });
check('wishlist page: two server rows render', !!list && list.rows.length === 2 && list.rows.some((r) => r.includes('أساسيات الجبر')) && list.rows.some((r) => r.includes('فيزياء أولى ثانوي')));
check('wishlist page: free course shows Free label, priced one shows the price', !!list && list.rows.some((r) => r.includes('250')) && list.rows.some((r) => r.includes('مجاني')));
check('wishlist page: no horizontal overflow at 390px', !!list && list.overflow <= 1);
await p.screenshot({ path: path.join(out, 'wishlist-mobile-ar.png') });

// move to cart: adds to the cart AND removes from the wishlist, row disappears
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-wishlist')].find((e) => e.offsetParent !== null); const row = [...pg.querySelectorAll('[data-testid="wishlist-item"]')].find((r) => r.textContent.includes('أساسيات الجبر')); row.querySelector('ion-button').dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(1000);
check('move to cart: POST /cart/items then DELETE /wishlist', calls.some((c) => c.method === 'POST' && c.path === '/cart/items' && c.body.includes('"c-1"')) && calls.filter((c) => c.method === 'DELETE' && c.path === '/wishlist/c-1').length >= 1);
const after = await p.evaluate(() => { const pg = [...document.querySelectorAll('app-wishlist')].find((e) => e.offsetParent !== null); return pg ? pg.querySelectorAll('[data-testid="wishlist-item"]').length : -1; });
check('move to cart: row leaves the list', after === 1);
await p.screenshot({ path: path.join(out, 'wishlist-after-move-ar.png') });

// heart state on detail (final state, desktop)
await p.setViewport({ width: 1280, height: 800 });
await p.goto('http://127.0.0.1:4312/app/courses/c-1', { waitUntil: 'networkidle0' }); await sleep(1500);
await p.screenshot({ path: path.join(out, 'detail-heart-ar.png') });
check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
