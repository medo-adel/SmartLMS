// Course reviews: aggregate + list + write/update form on course-detail, report flow, staff moderation tab.
// Real built UI in headless Chrome, fixture HTTP (NOT the real backend). Asserts the server rows render,
// the form submits a PUT, a report posts with a reason, and moderation hide calls the API.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-reviews'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });

const course = { id: 'c-1', title: 'أساسيات الجبر', slug: 'algebra', shortDescription: null, level: 'Beginner', language: 'ar', estimatedHours: 6, pricingModel: 'Free', price: 0, currency: 'EGP', description: null, status: 'Published', averageRating: 4.5, ratingsCount: 2, sections: [{ id: 'sec-1', title: 'القسم الأول', description: null, sortOrder: 0, lessons: [{ id: 'les-1', title: 'مقدمة', lessonType: 'Video', durationSeconds: 300, sortOrder: 0, isFreePreview: true }] }] };
let reviews = { items: [
  { id: 'rv-1', userName: 'منى س.', rating: 5, body: 'شرح ممتاز', createdAtUtc: '2026-11-20T10:00:00Z', mine: false },
  { id: 'rv-2', userName: 'طالب تجريبي', rating: 4, body: 'كويس بس محتاج تمارين أكتر', createdAtUtc: '2026-11-21T10:00:00Z', mine: true },
], count: 2, average: 4.5, myReview: { id: 'rv-2', rating: 4, body: 'كويس بس محتاج تمارين أكتر', status: 'Published' } };
let reported = [{ reportId: 'rep-1', reason: 'سبام', reportedAtUtc: '2026-11-25T10:00:00Z', review: { id: 'rv-1', courseId: 'c-1', courseTitle: 'أساسيات الجبر', userName: 'منى س.', rating: 5, body: 'شرح ممتاز', status: 'Published' } }];
const calls = [];
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /courses/c-1': ok(course),
  'GET /courses/c-1/reviews': () => ok(reviews),
  'GET /courses/c-1/exams': ok([]),
  'GET /me/enrollments': ok([{ id: 'e-1', courseId: 'c-1', title: 'أساسيات الجبر', status: 'Active', progressPercent: 10 }]),
  'GET /courses/c-1/progress': ok({ courseId: 'c-1', progressPercent: 10, status: 'Active', lessons: [] }),
  'GET /wishlist': ok({ items: [] }),
  'GET /cart': ok({ items: [], subtotal: 0, currency: 'EGP' }),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
  'GET /reviews/reported': () => ok({ items: reported }),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function mkPage(roles) {
  const p = await (await browser.createBrowserContext()).newPage();
  await p.setViewport({ width: 390, height: 844 });
  p.on('pageerror', (e) => consoleProblems.push('pageerror: ' + e.message));
  p.setRequestInterception(true);
  p.on('request', (rq) => { void (async () => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (rq.method() === 'PUT' && pp === '/courses/c-1/review') { calls.push({ method: 'PUT', path: pp, body: rq.postData() ?? '' }); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ id: 'rv-2' })) }); }
    const rep = pp.match(/^\/reviews\/(rv-\d)\/reports$/);
    if (rep && rq.method() === 'POST') { calls.push({ method: 'POST', path: pp, body: rq.postData() ?? '' }); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ id: 'rep-2' })) }); }
    const hide = pp.match(/^\/reviews\/(rv-\d)\/hide$/);
    if (hide && rq.method() === 'POST') { calls.push({ method: 'POST', path: pp, body: '' }); reported = []; return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(null)) }); }
    const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
    const body = typeof f === 'function' ? f() : f;
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(body ?? ok({})) });
  })().catch((e) => { consoleProblems.push('fixture: ' + e.message); rq.abort(); }); });
  await p.evaluateOnNewDocument((r) => {
    localStorage.setItem('CapacitorStorage.lang', 'ar');
    localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
    localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: 'u-1', tenantId: 't-1', fullName: 'طالب تجريبي', email: 'x@demo.test', roles: r } }));
  }, roles);
  return p;
}
const consoleProblems = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };

// student view: aggregate in hero, both reviews render, own review prefilled in the form
const p = await mkPage(['Student']);
await p.goto('http://127.0.0.1:4312/app/courses/c-1', { waitUntil: 'networkidle0' }); await sleep(1500);
const st = await p.evaluate(() => {
  const pg = document.querySelector('app-course-detail');
  const sum = pg.querySelector('[data-testid="rating-summary"]');
  const rows = [...pg.querySelectorAll('[data-testid="review-item"]')].map((r) => r.textContent);
  const sel = pg.querySelector('[data-testid="review-form"] select');
  const ta = pg.querySelector('[data-testid="review-form"] textarea');
  const overflow = document.documentElement.scrollWidth - document.documentElement.clientWidth;
  return { sum: sum?.textContent ?? null, rows, rating: sel?.value, body: ta?.value, overflow };
});
check('hero: aggregate summary renders (4.5, 2 reviews)', !!st.sum && st.sum.includes('4.5') && st.sum.includes('2'));
check('reviews: both server rows render with stars', st.rows.length === 2 && st.rows[0].includes('منى س.') && st.rows[0].includes('★★★★★') && st.rows[1].includes('كويس بس محتاج تمارين أكتر'));
check('form: my review prefilled (4 stars + body)', st.rating === '4' && st.body === 'كويس بس محتاج تمارين أكتر');
check('no horizontal overflow at 390px', st.overflow <= 1);
await p.screenshot({ path: path.join(out, 'reviews-detail-mobile-ar.png') });

// update own review -> PUT with rating + body
await p.evaluate(() => { const pg = document.querySelector('app-course-detail'); const sel = pg.querySelector('[data-testid="review-form"] select'); sel.value = '5'; sel.dispatchEvent(new Event('change')); });
await sleep(300);
await p.evaluate(() => { const pg = document.querySelector('app-course-detail'); pg.querySelector('[data-testid="review-form"] ion-button[type="submit"], [data-testid="review-form"] ion-button').dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(1000);
check('update: PUT /courses/c-1/review sent with rating + body', calls.some((c) => c.method === 'PUT' && c.path === '/courses/c-1/review' && c.body.includes('"rating"') && c.body.includes('كويس')));

// report another user's review -> POST with reason
await p.evaluate(() => { const pg = document.querySelector('app-course-detail'); const row = [...pg.querySelectorAll('[data-testid="review-item"]')].find((r) => r.textContent.includes('منى س.')); row.querySelector('.report').dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(500);
await p.evaluate(() => { const pg = document.querySelector('app-course-detail'); const row = [...pg.querySelectorAll('[data-testid="review-item"]')].find((r) => r.textContent.includes('منى س.')); const inp = row.querySelector('input.fld'); inp.value = 'محتوى مخالف'; inp.dispatchEvent(new Event('input')); });
await sleep(300);
await p.evaluate(() => { const pg = document.querySelector('app-course-detail'); const row = [...pg.querySelectorAll('[data-testid="review-item"]')].find((r) => r.textContent.includes('منى س.')); row.querySelector('ion-button[type="submit"]').dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(1000);
check('report: POST /reviews/rv-1/reports with reason', calls.some((c) => c.method === 'POST' && c.path === '/reviews/rv-1/reports' && c.body.includes('مخالف')));

// staff view: moderation tab lists the report; hide calls the API and empties the queue
const a = await mkPage(['TenantAdmin']);
await a.goto('http://127.0.0.1:4312/app/community', { waitUntil: 'networkidle0' }); await sleep(1500);
await a.evaluate(() => { document.querySelector('[data-testid="moderation-tab"]').dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(1200);
const mod = await a.evaluate(() => { const sec = document.querySelector('[data-testid="moderation"]'); return sec ? [...sec.querySelectorAll('[data-testid="report-item"]')].map((r) => r.textContent) : null; });
check('moderation: open report renders with course, review and reason', !!mod && mod.length === 1 && mod[0].includes('أساسيات الجبر') && mod[0].includes('شرح ممتاز') && mod[0].includes('سبام'));
await a.setViewport({ width: 390, height: 844 }); await sleep(700);
await a.screenshot({ path: path.join(out, 'moderation-mobile-ar.png') });
await a.evaluate(() => { const sec = document.querySelector('[data-testid="moderation"]'); [...sec.querySelectorAll('ion-button')].find((b) => b.textContent.includes('إخفاء')).dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(1000);
check('moderation: hide POST sent, queue emptied', calls.some((c) => c.method === 'POST' && c.path === '/reviews/rv-1/hide'));

check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
