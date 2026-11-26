// Timed-transcript verification. Real built UI in headless Chrome against contract fixtures
// (NOT the real backend). Asserts the captions + transcript-seek experience:
//  1. A video lesson with a Whisper transcript gets CC and Transcript buttons; captions start ON.
//  2. The captions overlay follows the playhead (Arabic RTL cue text over the video).
//  3. The CC button hides/shows the overlay.
//  4. The transcript panel lists every cue with its time, highlights the active one, and
//     clicking a cue seeks the video to that moment.
//  5. A lesson without a transcript (404) shows no CC/Transcript buttons and no errors.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-transcript'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json', '.webm': 'video/webm' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });

const lessons = [
  { id: 'les-1', title: 'الدرس الأول', lessonType: 'Video', durationSeconds: 120, sortOrder: 0, isFreePreview: false },
  { id: 'les-2', title: 'الدرس الصوتي', lessonType: 'Audio', durationSeconds: 90, sortOrder: 1, isFreePreview: false },
];
const mkLesson = (l) => ({ id: l.id, courseId: 'c-1', sectionId: 'sec-1', title: l.title, lessonType: l.lessonType, contentMarkdown: null, externalUrl: null, durationSeconds: l.durationSeconds, isFreePreview: l.isFreePreview });
const course = { id: 'c-1', title: 'كورس التجربة', slug: 'test-course', shortDescription: null, level: 'Beginner', language: null, estimatedHours: null, pricingModel: 'Free', price: 0, currency: 'EGP', description: null, status: 'Published', sections: [
  { id: 'sec-1', title: 'القسم الأول', description: null, sortOrder: 0, lessons },
] };
const cues = [ // inside the 6s fixture clip so the browser never clamps a seek
  { start: 0, end: 2, text: 'أهلاً بكم في الدرس الأول من الكورس' },
  { start: 2, end: 4, text: 'اليوم هنتكلم عن أساسيات البرمجة خطوة بخطوة' },
  { start: 4, end: 6, text: 'يلا نبدأ مع بعض ونشوف أول مثال عملي' },
];
const transcript = { cueCount: cues.length, cues, generatedAtUtc: '2026-11-20T10:00:00Z' };
const transcriptGets = [];
const misses = [];
const webm = fs.readFileSync(path.resolve('tools/mock-lesson.webm'));

const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /learning-paths/manage': ok([]), 'GET /learning-paths': ok([]), 'GET /courses/mine': ok([]),
  'GET /courses': ok({ items: [], totalCount: 0 }),
  'GET /courses/c-1': ok(course),
  'GET /courses/c-1/progress': ok({ courseId: 'c-1', progressPercent: 0, status: 'Active', lessons: [] }),
  'GET /cart': ok({ items: [], subtotal: 0, currency: 'EGP' }),
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
  if (rq.method() === 'GET' && les && !les[2]) return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(mkLesson(lessons.find((l) => l.id === les[1])))) });
  if (rq.method() === 'GET' && les && les[2] === '/stream') return rq.respond({ status: 200, headers: { ...h, 'content-type': 'video/webm' }, body: webm });
  if (rq.method() === 'GET' && les && les[2] === '/transcript') {
    transcriptGets.push(les[1]);
    if (les[1] === 'les-2') return rq.respond({ status: 404, headers: h, body: JSON.stringify({ success: false, data: null, message: 'TRANSCRIPT_NOT_FOUND', errors: [] }) }); // not transcribed yet
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(transcript)) });
  }
  if (rq.method() === 'PUT' && les && les[2] === '/progress') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({})) });
  if (rq.method() === 'GET' && les) return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok([])) }); // chapters/notes/bookmarks/quizzes/resources
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  if (!f) misses.push(rq.method() + ' ' + pp);
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
const pg = () => p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document;
  const v = pg.querySelector('video, audio');
  const cueEls = [...pg.querySelectorAll('.ts-cue')];
  return {
    url: location.pathname,
    ccToggle: !!pg.querySelector('[data-testid="cc-toggle"]'),
    tsToggle: !!pg.querySelector('[data-testid="transcript-toggle"]'),
    overlay: pg.querySelector('[data-testid="cc-overlay"]')?.textContent.trim() ?? '',
    cues: cueEls.map((e) => e.textContent.trim()),
    highlighted: cueEls.findIndex((e) => e.classList.contains('on')),
    time: v?.currentTime ?? -1,
  };
});
const setTime = (t) => p.evaluate((t) => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; const v = pg.querySelector('video, audio'); v.currentTime = t; v.dispatchEvent(new Event('timeupdate')); }, t);

await p.goto('http://127.0.0.1:4312/app/learn/c-1/les-1', { waitUntil: 'networkidle0' }); await sleep(3000);

// 1. transcript loaded once, CC + transcript buttons render, captions start ON
let s = await pg();
if (process.env.DEBUG) { console.log('DEBUG first state', JSON.stringify(s)); console.log('DEBUG body', (await p.evaluate(() => document.body.innerText.slice(0, 400)))); console.log('DEBUG transcriptGets', transcriptGets.join(',')); console.log('DEBUG consoleProblems', JSON.stringify(consoleProblems)); console.log('DEBUG misses', JSON.stringify(misses)); console.log('DEBUG app', await p.evaluate(() => { const ap = document.querySelector('app-player'); return JSON.stringify({ player: !!ap, playerLen: ap?.innerHTML.length ?? 0, outlet: document.querySelector('ion-router-outlet')?.innerHTML.length ?? -1, bodyLen: document.body.innerHTML.length }); })); }
check('transcript fetched for the video lesson', transcriptGets.filter((x) => x === 'les-1').length === 1);
check('CC and Transcript buttons render', s.ccToggle && s.tsToggle);
await setTime(2.5); await sleep(400);
s = await pg();
check('captions start ON with the cue under the playhead', s.overlay === cues[1].text);
await p.screenshot({ path: path.join(out, 'captions-desktop-ar.png') });

// 2. playhead past the last cue -> third caption; CC toggle hides the overlay
await setTime(4.5); await sleep(400);
s = await pg();
check('captions follow the playhead (third cue)', s.overlay === cues[2].text);
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; pg.querySelector('[data-testid="cc-toggle"]').dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(400);
s = await pg();
check('CC toggle hides the overlay', s.overlay === '');

// 3. transcript panel: lists every cue, highlights the active one, click seeks
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; pg.querySelector('[data-testid="transcript-toggle"]').dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(500);
s = await pg();
check('panel lists all 3 cues with times', s.cues.length === 3 && /0:02/.test(s.cues[1]) && s.cues[0].includes(cues[0].text));
check('active cue highlighted in the panel', s.highlighted === 2);
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; [...pg.querySelectorAll('.ts-cue')][0].dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(800);
s = await pg();
check('clicking a cue seeks the video back to its start', s.time >= 0 && s.time < 1.8); // seek() also plays, so the head moves on a little
check('panel highlight follows the seek', s.highlighted === 0);
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; pg.querySelector('[data-testid="transcript-panel"]')?.scrollIntoView({ block: 'center' }); }); await sleep(400);
await p.screenshot({ path: path.join(out, 'transcript-panel-desktop-ar.png') });

// 4. mobile viewport proof: captions + panel in 390px RTL
await p.setViewport({ width: 390, height: 844 });
await p.goto('http://127.0.0.1:4312/app/learn/c-1/les-1', { waitUntil: 'networkidle0' }); await sleep(3000);
await setTime(2.5); await sleep(400);
s = await pg();
check('mobile: captions overlay renders in RTL', s.overlay === cues[1].text);
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; pg.querySelector('[data-testid="transcript-toggle"]').dispatchEvent(new MouseEvent('click', { bubbles: true })); }); await sleep(500);
await p.evaluate(() => { const pg = [...document.querySelectorAll('app-player')].find((e) => e.offsetParent !== null) ?? document; pg.querySelector('[data-testid="transcript-panel"]')?.scrollIntoView({ block: 'center' }); }); await sleep(400);
await p.screenshot({ path: path.join(out, 'transcript-mobile-ar.png') });

// 5. audio lesson without a transcript: no buttons, no overlay, no errors
await p.setViewport({ width: 1440, height: 900 });
await p.goto('http://127.0.0.1:4312/app/learn/c-1/les-2', { waitUntil: 'networkidle0' }); await sleep(3000);
s = await pg();
check('audio lesson without transcript: no CC/Transcript buttons', s.url.endsWith('/les-2') && !s.ccToggle && !s.tsToggle && s.overlay === '');
check('the 404 transcript was attempted once and swallowed', transcriptGets.filter((x) => x === 'les-2').length === 1);

check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));
console.log(results.join('\n'));
await browser.close(); server.close();
