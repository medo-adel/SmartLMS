// Chat + live sessions + learning paths verification. Real UI in headless Chrome against contract fixtures (NOT the real backend).
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-chat-live'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4308);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const instructor = { id: 'u-in', tenantId: TID, fullName: 'Dr. Karim Nabil', email: 'karim@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null };
const student = { id: 'u-st', tenantId: TID, fullName: 'Omar Khaled', email: 'omar@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null };
const captured = [];
const liveRow = { id: 'lv1', title: 'Revision: quadratics', provider: 'Zoom', startsAtUtc: '2026-11-26T16:00:00Z', endsAtUtc: '2026-11-26T17:00:00Z', status: 'Scheduled', joinUrl: 'https://zoom.us/j/123' };
// Real API shape: newest first, 50 per page, nextBefore cursor for older pages.
const page1 = { messages: [
  { id: 'm2', senderId: 'u-in', senderName: 'Dr. Karim Nabil', body: 'Yes, I will share the link.', sentAtUtc: '2026-11-25T10:05:00Z' },
  { id: 'm1', senderId: 'u-st', senderName: 'Omar Khaled', body: 'Is the revision session recorded?', sentAtUtc: '2026-11-25T10:00:00Z' } ], nextBefore: '2026-11-25T10:00:00Z' };
const page2 = { messages: [
  { id: 'm0', senderId: 'u-st', senderName: 'Omar Khaled', body: 'Earlier question about homework 3.', sentAtUtc: '2026-11-24T09:00:00Z' } ], nextBefore: null };
const pathRow = { id: 'lp1', title: 'Math foundations', slug: 'math-foundations-a1b2c3', description: 'From algebra to calculus.' };
const pathDetail = { id: 'lp1', title: 'Math foundations', description: 'From algebra to calculus.', status: 'Published', issuesCertificate: true,
  courses: [ { id: 'c1', title: 'Algebra II', sortOrder: 1, isMandatory: true } ] };
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'Published', ownerInstructorId: 'u-in' }]),
  'GET /me/enrollments': ok([{ id: 'e1', courseId: 'c1', title: 'Algebra II', status: 'Active', progressPercent: 40 }]),
  'GET /announcements': ok([]),
  'GET /chat/rooms': ok([{ id: 'r1', name: 'Algebra II', roomType: 'Course', courseId: 'c1' }]),
  'GET /courses/c1/live-sessions': ok([liveRow]),
  'GET /live-sessions/lv1/attendance': ok([{ userId: 'u-st', fullName: 'Omar Khaled', joinedAtUtc: '2026-11-25T12:00:00Z' }]),
  'POST /live-sessions/lv1/join': ok({ joinUrl: 'https://zoom.us/j/123' }),
  'GET /learning-paths': ok([pathRow]),
  'GET /learning-paths/lp1': ok(pathDetail),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const dyn = () => ok({ id: 'new-id' });
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(user, lang, viewport) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport(viewport ?? { width: 1280, height: 900 });
  await p.setRequestInterception(true);
  p.on('request', (rq) => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    captured.push({ method: rq.method(), path: pp, body: rq.postData() ?? null });
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (pp === '/chat/rooms/r1/messages' && rq.method() === 'GET') {
      return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(u.searchParams.has('before') ? page2 : page1)) });
    }
    const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? dyn()) });
  });
  await p.evaluateOnNewDocument((l, u) => {
    localStorage.setItem('CapacitorStorage.lang', l);
    localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
    localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
  }, lang, user);
  return p;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const find = (method, pathRe) => captured.filter((c) => c.method === method && pathRe.test(c.path));
const setInput = (p, name, value) => p.evaluate((n, v) => { const inp = document.querySelector(`input[name="${n}"], textarea[name="${n}"]`); inp.value = v; inp.dispatchEvent(new Event('input', { bubbles: true })); }, name, value);
const clickText = (p, sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim().includes(t)); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);
const chatState = (p) => p.evaluate(() => [...document.querySelectorAll('.chatmsg')].map((e) => e.textContent || ''));
const noOverflow = (p) => p.evaluate(() => document.scrollingElement.scrollWidth <= document.scrollingElement.clientWidth + 1);

// ---- instructor: chat chronology, live create, attendance ----
const p = await page(instructor, 'en');
await p.goto('http://127.0.0.1:4308/app/community', { waitUntil: 'networkidle0' }); await sleep(900);
await clickText(p, 'button', 'Chat'); await sleep(700);
check('rooms listed', find('GET', /^\/chat\/rooms$/).length > 0);
await clickText(p, 'button.pn', 'Algebra II'); await sleep(800);
check('first page renders oldest-first (question above answer)', (await chatState(p)).join('|').indexOf('recorded') < (await chatState(p)).join('|').indexOf('share the link'));
await clickText(p, 'ion-button', 'Load older'); await sleep(700);
const afterOlder = await chatState(p);
check('load older prepends older messages, no duplicates', afterOlder.length === 3 && new Set(afterOlder).size === 3 && afterOlder[0].includes('homework 3'));
await setInput(p, 'chatBody', 'See you at the session tomorrow.');
await p.evaluate(() => { document.querySelector('textarea[name="chatBody"]').closest('form').requestSubmit(); }); await sleep(800);
const sent = find('POST', /^\/chat\/rooms\/r1\/messages$/).at(-1);
check('chat POSTs { body }', !!sent && sent.body.includes('See you at the session tomorrow.'));
const afterSend = await chatState(p);
check('after send, log stays chronological without duplicates', afterSend.length === 3 && new Set(afterSend).size === 3 && afterSend.at(-1).includes('share the link'));
check('no horizontal overflow (desktop en)', await noOverflow(p));
await p.screenshot({ path: path.join(out, 'chat-room-desktop-en.png') }); console.log('shot chat-room-desktop-en');

await clickText(p, 'button', 'Live sessions'); await sleep(500);
await p.evaluate(() => { const el = document.querySelector('select[name="liveCourse"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(800);
check('live sessions listed', find('GET', /^\/courses\/c1\/live-sessions$/).length > 0);
await setInput(p, 'lvTitle', 'Q&A before finals'); await setInput(p, 'lvUrl', 'https://meet.google.com/abc-defg-hij');
await setInput(p, 'lvStart', '2026-11-27T18:00'); await setInput(p, 'lvEnd', '2026-11-27T19:00');
await p.evaluate(() => { const f = document.querySelector('input[name="lvUrl"]').closest('form'); f.requestSubmit(); }); await sleep(800);
const lv = find('POST', /^\/courses\/c1\/live-sessions$/).at(-1);
const lvBody = lv ? JSON.parse(lv.body) : {};
check('live POSTs https joinUrl + ISO startsAt + provider', !!lv && /^https:\/\//.test(lvBody.joinUrl ?? '') && /T.*Z$/.test(lvBody.startsAtUtc ?? '') && lvBody.provider === 'Zoom');
await clickText(p, 'ion-button', 'Attendance'); await sleep(700);
check('attendance GET for staff', find('GET', /^\/live-sessions\/lv1\/attendance$/).length > 0);
await p.evaluate(() => { const card = [...document.querySelectorAll('.pn')].find((e) => (e.textContent || '').includes('Revision: quadratics')); card?.scrollIntoView({ block: 'start' }); }); await sleep(300);
await p.screenshot({ path: path.join(out, 'live-attendance-desktop-en.png') }); console.log('shot live-attendance-desktop-en');
check('attendance row visible in DOM', await p.evaluate(() => document.body.textContent.includes('Omar Khaled') && document.body.textContent.includes('11/25/26')));

// paths (instructor) on the dedicated /app/paths page: list, detail, create draft (no publish)
await p.goto('http://127.0.0.1:4308/app/paths', { waitUntil: 'networkidle0' }); await sleep(1100);
check('paths listed', find('GET', /^\/learning-paths$/).length > 0);
check('path detail loaded', find('GET', /^\/learning-paths\/lp1$/).length > 0);
check('instructor form hides Published option', await p.evaluate(() => { const o = document.querySelector('select[name="pfStatus"]'); return o && ![...o.options].some((x) => x.value === 'Published'); }));
await setInput(p, 'pfTitle', 'Science track'); await p.evaluate(() => { document.querySelector('input[name^="pfc-"]')?.click(); });
await p.evaluate(() => { document.querySelector('input[name="pfTitle"]').closest('form').requestSubmit(); }); await sleep(900);
const pc = find('POST', /^\/learning-paths$/).at(-1);
const pcBody = pc ? JSON.parse(pc.body) : {};
check('path create POSTs title+status Draft+courseIds', !!pc && pcBody.title === 'Science track' && pcBody.status === 'Draft' && Array.isArray(pcBody.courseIds) && pcBody.courseIds.length === 1);
await p.screenshot({ path: path.join(out, 'paths-desktop-en.png') }); console.log('shot paths-desktop-en');

// ---- student: join fallback link, path enroll ----
const p2 = await page(student, 'en');
await p2.goto('http://127.0.0.1:4308/app/community', { waitUntil: 'networkidle0' }); await sleep(900);
await clickText(p2, 'button', 'Live sessions'); await sleep(500);
await p2.evaluate(() => { const el = document.querySelector('select[name="liveCourse"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(800);
check('student sees no schedule form', (await p2.$('input[name="lvUrl"]')) === null);
await clickText(p2, 'ion-button', 'Join session'); await sleep(800);
check('student join POSTs /live-sessions/lv1/join', find('POST', /^\/live-sessions\/lv1\/join$/).length > 0);
const href = await p2.evaluate(() => document.querySelector('a.btn-link')?.href ?? null);
check('popup-safe explicit Open session link carries the returned joinUrl', href === 'https://zoom.us/j/123');
await p2.screenshot({ path: path.join(out, 'live-student-desktop-en.png') }); console.log('shot live-student-desktop-en');
await p2.goto('http://127.0.0.1:4308/app/paths', { waitUntil: 'networkidle0' }); await sleep(1100);
await clickText(p2, 'ion-button', 'Enroll'); await sleep(700);
check('student enroll POSTs /learning-paths/lp1/enroll', find('POST', /^\/learning-paths\/lp1\/enroll$/).length > 0);

// ---- RTL + mobile overflow check on chat ----
const p3 = await page(student, 'ar', { width: 390, height: 844 });
await p3.goto('http://127.0.0.1:4308/app/community', { waitUntil: 'networkidle0' }); await sleep(1000);
await clickText(p3, 'button', 'المحادثات'); await sleep(600);
await clickText(p3, 'button.pn', 'Algebra II'); await sleep(800);
check('no horizontal overflow (mobile ar/rtl)', await noOverflow(p3));
await p3.screenshot({ path: path.join(out, 'chat-room-mobile-ar.png') }); console.log('shot chat-room-mobile-ar');

console.log('\n' + results.join('\n'));
console.log(process.exitCode ? 'VERIFY FAILED' : 'VERIFY PASSED');
await browser.close(); server.close();
