// Player curriculum + manual-advance verification. Real built UI in headless Chrome against contract fixtures
// (NOT the real backend). Asserts the Udemy-style learner-controlled experience:
//  1. The course map lists every section/lesson, highlights the current one and marks completed lessons.
//  2. Each lesson's downloadable files show under it and download through the download endpoint.
//  3. When the video ends, progress is recorded as completed but the page does NOT navigate by itself;
//     an "up next" card offers the next lesson and only a learner click moves on.
//  4. Clicking a lesson in the map opens it (component reloads for the new lesson, not a stale view).
//  5. Shell cart badge shows the item count from the API on load (fixture: 2).
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-player-curriculum'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json', '.webm': 'video/webm' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });

const lessons = [
  { id: 'les-1', title: 'Lesson One', lessonType: 'Video', durationSeconds: 120, sortOrder: 0, isFreePreview: false },
  { id: 'les-2', title: 'Lesson Two', lessonType: 'Article', durationSeconds: null, sortOrder: 1, isFreePreview: false },
  { id: 'les-3', title: 'Lesson Three', lessonType: 'Pdf', durationSeconds: null, sortOrder: 0, isFreePreview: false },
];
const mkLesson = (l) => ({ id: l.id, courseId: 'c-1', sectionId: 'sec-1', title: l.title, lessonType: l.lessonType, contentMarkdown: null, externalUrl: null, durationSeconds: l.durationSeconds, isFreePreview: l.isFreePreview });
const course = { id: 'c-1', title: 'Test Course', slug: 'test-course', shortDescription: null, level: 'Beginner', language: null, estimatedHours: null, pricingModel: 'Free', price: 0, currency: 'EGP', description: null, status: 'Published', sections: [
  { id: 'sec-1', title: 'Section One', description: null, sortOrder: 0, lessons: [lessons[0], lessons[1]] },
  { id: 'sec-2', title: 'Section Two', description: null, sortOrder: 1, lessons: [lessons[2]] },
] };
const progressPuts = [];
const downloads = [];
const cart = { items: [{ id: 'ci-1', itemType: 'Course', itemId: 'c-9', description: 'Another course', price: 100, currency: 'EGP', available: true }, { id: 'ci-2', itemType: 'Course', itemId: 'c-10', description: 'Third course', price: 50, currency: 'EGP', available: true }], subtotal: 150, currency: 'EGP' };
const resources = { 'les-2': [{ id: 'r-1', title: 'slides.pdf', isDownloadable: true, contentType: 'application/pdf', sizeBytes: 93871, fileName: 'slides.pdf' }] };
const webm = fs.readFileSync(path.resolve('tools/mock-lesson.webm'));

const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /learning-paths/manage': ok([]), 'GET /learning-paths': ok([]), 'GET /courses/mine': ok([]),
  'GET /courses': ok({ items: [], totalCount: 0 }),
  'GET /courses/c-1': ok(course),
  'GET /courses/c-1/progress': ok({ courseId: 'c-1', progressPercent: 33, status: 'Active', lessons: [{ lessonId: 'les-1', status: 'Completed', lastPositionSeconds: 120, watchedSeconds: 120 }, { lessonId: 'les-2', status: 'InProgress', lastPositionSeconds: 0, watchedSeconds: 0 }] }),
  'GET /cart': ok(cart),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox', '--autoplay-policy=no-user-gesture-required'] });
const p = await (await browser.createBrowserContext()).newPage();
await p.setViewport({ width: 1440, height: 900 });
const consoleProblems = [];
p.on('pageerror', (e) => consoleProblems.push('pageerror: ' + e.message));
p.setRequestInterception(true);
p.on('request', (rq) => { void (async () => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  const les = pp.match(/^\/lessons\/(les-\d)(\/.*)?$/);
  if (les && les[1] === 'les-3' && rq.method() === 'GET' && (!les[2] || les[2] === '/resources' || les[2] === '/stream')) {
    return rq.respond({ status: 403, headers: h, body: JSON.stringify({ success: false, data: null, message: 'LESSON_LOCKED_DRIP', errors: [] }) }); // locked like the real backend
  }
  if (rq.method() === 'GET' && les && !les[2]) return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(mkLesson(lessons.find((l) => l.id === les[1])))) });
  if (rq.method() === 'GET' && les && les[2] === '/stream') return rq.respond({ status: 200, headers: { ...h, 'content-type': 'video/webm' }, body: webm });
  if (rq.method() === 'GET' && les && les[2] === '/resources') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(resources[les[1]] ?? [])) });
  if (rq.method() === 'GET' && les && les[2] === '/resources/r-1/download') { downloads.push(les[1]); return rq.respond({ status: 200, headers: { ...h, 'content-type': 'application/pdf' }, body: Buffer.from('%PDF fixture') }); }
  if (rq.method() === 'PUT' && les && les[2] === '/progress') { progressPuts.push({ lesson: les[1], body: rq.postData() ?? '' }); return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({})) }); }
  if (rq.method() === 'GET' && les) return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok([])) }); // chapters/notes/bookmarks/quizzes
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
})().catch((e) => { consoleProblems.push('handler: ' + e.message); try { rq.abort(); } catch { /* handled */ } }); });
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

await p.goto('http://127.0.0.1:4312/app/learn/c-1/les-1', { waitUntil: 'networkidle0' }); await sleep(2500);

// 1. curriculum map renders with sections, lessons, current highlight, completion mark
const map = await p.evaluate(() => ({
  sections: [...document.querySelectorAll('.cur-sec strong')].map((e) => e.textContent.trim()),
  links: [...document.querySelectorAll('.cur-les .cur-link')].length,
  current: document.querySelector('.cur-les.on .cur-ti')?.textContent.trim() ?? '',
  done: [...document.querySelectorAll('.cur-check.done')].length,
  percent: document.querySelector('.cur-head .mut')?.textContent.trim() ?? '',
}));
check('map lists both sections', map.sections.join(',').includes('Section One') && map.sections.join(',').includes('Section Two'));
check('map lists all 3 lessons', map.links === 3);
check('current lesson highlighted (Lesson One)', map.current.includes('Lesson One'));
check('completed lesson marked done', map.done === 1);
check('course progress percent shown (33%)', map.percent.includes('33'));

// 2. lesson files listed under their lesson and downloadable
const fileRow = await p.evaluate(() => document.querySelector('.cur-les:nth-child(odd) .cur-file')?.textContent ?? [...document.querySelectorAll('.cur-file')].map((e) => e.textContent.trim()).join('|'));
check('downloadable file listed under Lesson Two', /slides\.pdf/.test(fileRow) && /92 KB/.test(fileRow));
await p.evaluate(() => [...document.querySelectorAll('.cur-file')].find((e) => e.textContent.includes('slides.pdf'))?.click());
await sleep(600);
check('file downloads through the download endpoint', downloads.length === 1 && downloads[0] === 'les-2');

await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; pg.scrollIntoView({ block: 'start' }); window.scrollTo(0, 0); }); await sleep(400);
await p.screenshot({ path: path.join(out, 'player-map-desktop-ar.png') });

// 3. video end: completion recorded, NO automatic navigation, up-next card offers the choice
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; const v = pg.querySelector('video'); v.dispatchEvent(new Event('ended')); });
await sleep(1200);
const afterEnd = await p.evaluate(() => ({ url: location.pathname, upNext: document.querySelector('.upnext')?.textContent ?? '' }));
check('ending recorded completion (PUT completed=true)', progressPuts.some((x) => x.lesson === 'les-1' && /"completed":true/.test(x.body)));
check('no automatic navigation after the video ended', afterEnd.url === '/app/learn/c-1/les-1');
check('up-next card offers Lesson Two', /Lesson Two/.test(afterEnd.upNext));

await p.evaluate(() => document.querySelector('.upnext')?.scrollIntoView({ block: 'center' })); await sleep(400);
await p.screenshot({ path: path.join(out, 'player-curriculum-desktop-ar.png') });

// 4a. learner chooses next via the up-next card -> navigates and reloads the new lesson
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; const b = pg.querySelector('.upnext ion-button'); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(2500);
const afterNext = await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; return { url: location.pathname, title: pg.querySelector('.head h1')?.textContent.trim() ?? '', current: pg.querySelector('.cur-les.on .cur-ti')?.textContent.trim() ?? '' }; });
check('up-next click navigates to Lesson Two', afterNext.url === '/app/learn/c-1/les-2');
check('player reloaded for the new lesson (not stale)', afterNext.title === 'Lesson Two' && afterNext.current.includes('Lesson Two'));

// 4b. the map never lists files for a locked lesson, and opening it shows the lock screen (backend refused)
const lockedRow = await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; const row = [...pg.querySelectorAll('.cur-les')].find((e) => e.textContent.includes('Lesson Three')); return row?.querySelectorAll('.cur-file').length ?? -1; });
check('locked lesson shows no files in the map', lockedRow === 0);
// rapid choice: Two then immediately Three — a stale response must not paint over the final lesson
await p.evaluate(() => { // both clicks in one context: the first navigation must not stop the second choice
  const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document;
  const two = [...pg.querySelectorAll('.cur-les .cur-link')].find((e) => e.textContent.includes('Lesson Two'));
  const three = [...pg.querySelectorAll('.cur-les .cur-link')].find((e) => e.textContent.includes('Lesson Three'));
  two?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  three?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
});
await sleep(2500);
const afterPick = await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; return { url: location.pathname, lock: (pg.textContent || '').includes('الدرس مقفول'), stale: (pg.querySelector('.head h1')?.textContent ?? '').includes('Lesson Two') }; });
check('locked lesson opens the lock screen, not the content', afterPick.url === '/app/learn/c-1/les-3' && afterPick.lock);
check('rapid lesson switches leave no stale lesson rendered', !afterPick.stale);

// 5. cart badge from the API on load
const badge = await p.evaluate(() => document.querySelector('ion-item[ng-reflect-router-link="/app/cart"] .count, ion-item .count')?.textContent?.trim() ?? '');
check('cart badge shows API count (2)', badge === '2');

// mobile viewport proof: fresh page load so no stacked-page artifacts
await p.setViewport({ width: 390, height: 844 });
await p.goto('http://127.0.0.1:4312/app/learn/c-1/les-1', { waitUntil: 'networkidle0' }); await sleep(2500);
await p.screenshot({ path: path.join(out, 'player-mobile-collapsed-ar.png') });
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; const b = [...pg.querySelectorAll('ion-button')].find((x) => (x.textContent || '').includes('محتوى الكورس')); b?.dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(600);
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; pg.querySelector('.cur')?.scrollIntoView({ block: 'start' }); }); await sleep(400);
await p.screenshot({ path: path.join(out, 'player-curriculum-mobile-ar.png') });

check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
