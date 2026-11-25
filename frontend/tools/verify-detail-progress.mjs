// Course-detail per-lesson progress + PDF-view completion. Real built UI in headless Chrome, fixture HTTP
// (NOT the real backend). Asserts: the detail page shows the persisted overall bar, a done check for a
// completed lesson, a fractional ring for a half-watched one, nothing for an untouched one, all unchanged
// after reload; and that the player records completion only when a PDF actually loads (never on a failed load).
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-detail-progress'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });

const lessons = [
  { id: 'les-1', title: 'Lesson One', lessonType: 'Video', durationSeconds: 120, sortOrder: 0, isFreePreview: false },
  { id: 'les-2', title: 'Lesson Two', lessonType: 'Pdf', durationSeconds: 600, sortOrder: 1, isFreePreview: false },
  { id: 'les-3', title: 'Lesson Three', lessonType: 'Article', durationSeconds: null, sortOrder: 2, isFreePreview: false },
  { id: 'les-4', title: 'Lesson Four', lessonType: 'Pdf', durationSeconds: 300, sortOrder: 3, isFreePreview: false },
];
const course = { id: 'c-1', title: 'Test Course', slug: 'test-course', shortDescription: null, level: 'Beginner', language: null, estimatedHours: null, pricingModel: 'Free', price: 0, currency: 'EGP', description: null, status: 'Published', sections: [{ id: 'sec-1', title: 'Section One', description: null, sortOrder: 0, lessons }] };
const progress = { courseId: 'c-1', progressPercent: 33.3, status: 'Active', lessons: [
  { lessonId: 'les-1', status: 'InProgress', lastPositionSeconds: 60, watchedSeconds: 60 },
  { lessonId: 'les-2', status: 'Completed', lastPositionSeconds: 0, watchedSeconds: 0 },
] };
const puts = []; // captured progress PUTs { lesson, body }
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /courses/c-1': ok(course),
  'GET /courses/c-1/progress': ok(progress),
  'GET /courses/c-1/exams': ok([]),
  'GET /me/enrollments': ok([{ id: 'e-1', courseId: 'c-1', courseTitle: 'Test Course', status: 'Active', progressPercent: 33.3 }]),
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
  const put = pp.match(/^\/lessons\/(les-\d)\/progress$/);
  if (rq.method() === 'PUT' && put) { puts.push({ lesson: put[1], body: rq.postData() ?? '' }); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(progress)) }); }
  const les = pp.match(/^\/lessons\/(les-\d)(\/.*)?$/);
  if (les && rq.method() === 'GET' && !les[2]) return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ ...lessons.find((l) => l.id === les[1]), contentMarkdown: 'body', externalUrl: null })) });
  if (les && rq.method() === 'GET' && les[2] === '/stream') {
    if (les[1] === 'les-4') return rq.respond({ status: 404, headers: h, body: JSON.stringify({ success: false, data: null, message: 'missing', errors: [] }) });
    return rq.respond({ status: 200, headers: { ...h, 'content-type': 'application/pdf' }, body: Buffer.from('%PDF-1.4 fixture') });
  }
  if (les && rq.method() === 'GET' && les[2] === '/resources') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok([])) });
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
})().catch((e) => { consoleProblems.push('handler: ' + e.message); try { rq.abort(); } catch { /* already handled */ } });
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

await p.goto('http://127.0.0.1:4312/app/courses/c-1', { waitUntil: 'networkidle0' }); await sleep(1500);
const read = () => p.evaluate(() => {
  const rows = [...document.querySelectorAll('.les')].map((r) => ({
    title: r.textContent,
    done: !!r.querySelector('.cur-check.done'),
    ring: r.querySelector('.ring')?.getAttribute('style') ?? null,
    started: !!r.querySelector('.ring.started'),
  }));
  const bar = document.querySelector('.course-prog .bar span')?.getAttribute('style') ?? '';
  const pct = document.querySelector('.course-prog small')?.textContent.trim() ?? '';
  return { rows, bar, pct };
});
let d = await read();
check('overall bar shows the persisted 33.3%', d.bar.includes('33.3') && d.pct.includes('33.3'));
check('completed lesson shows the done check', d.rows.some((r) => r.title.includes('Lesson Two') && r.done));
check('half-watched lesson shows a 50% ring', d.rows.some((r) => r.title.includes('Lesson One') && r.ring && r.ring.includes('conic-gradient') && r.ring.includes('50%')));
check('untouched lesson shows no progress mark', d.rows.some((r) => r.title.includes('Lesson Three') && !r.done && !r.ring && !r.started));
await p.screenshot({ path: path.join(out, 'detail-progress-ar.png') });

await p.reload({ waitUntil: 'networkidle0' }); await sleep(1500);
d = await read();
check('progress survives a full reload (from the API)', d.pct.includes('33.3') && d.rows.some((r) => r.title.includes('Lesson Two') && r.done));

// phone width: marks and bar stay inside the viewport
await p.setViewport({ width: 390, height: 844 }); await sleep(900);
const mob = await p.evaluate(() => ({ overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth, ring: !!document.querySelector('.ring'), done: !!document.querySelector('.cur-check.done') }));
check('mobile: marks render, no horizontal overflow', mob.ring && mob.done && mob.overflow <= 1);
await p.screenshot({ path: path.join(out, 'detail-progress-mobile-ar.png') });
await p.setViewport({ width: 1280, height: 800 });

// PDF successful view records completion; a failed load records nothing
await p.goto('http://127.0.0.1:4312/app/learn/c-1/les-2', { waitUntil: 'networkidle0' }); await sleep(2500);
const pdfPut = puts.find((x) => x.lesson === 'les-2');
check('PDF successful view sent completion', !!pdfPut && pdfPut.body.includes('"completed":true'));
await p.goto('http://127.0.0.1:4312/app/learn/c-1/les-4', { waitUntil: 'networkidle0' }); await sleep(2500);
check('failed PDF load sent no completion', !puts.some((x) => x.lesson === 'les-4'));
check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
