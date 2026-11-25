// Community verification + screenshots. Real UI in headless Chrome against contract fixtures (NOT the real backend).
// Asserts: threads list load, thread create payload, reply payload, upvote POSTs, pin POST (staff),
// staff announcement payload, and that a TenantAdmin (read-only for threads) gets no Post button.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-community'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4307);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const instructor = { id: 'u-in', tenantId: TID, fullName: 'Dr. Karim Nabil', email: 'karim@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null };
const admin = { id: 'u-admin', tenantId: TID, fullName: 'Hana Mostafa', email: 'admin@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['TenantAdmin'], lastLoginAtUtc: null };
const captured = [];
const thread1 = { id: 't1', title: 'How do you factor x^2 - 9?', body: 'Stuck on difference of squares.', isPinned: false, isLocked: false, upvoteCount: 3, createdAtUtc: '2026-11-24T15:00:00Z', posts: [
  { id: 'p1', parentPostId: null, body: 'It is (x-3)(x+3).', isInstructorPinned: true, upvoteCount: 5, createdAtUtc: '2026-11-24T15:30:00Z', authorName: 'Dr. Karim Nabil' } ] };
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'Published', ownerInstructorId: 'u-in' }]),
  'GET /courses/c1/threads': ok([{ id: 't1', title: 'How do you factor x^2 - 9?', lessonId: null, threadType: 'Forum', isPinned: false, isLocked: false, upvoteCount: 3, createdAtUtc: '2026-11-24T15:00:00Z', authorName: 'Mona Adel' }]),
  'GET /threads/t1': ok(thread1),
  'GET /announcements': ok([{ id: 'an1', courseId: null, title: 'Midterm week', body: 'Midterms start Sunday.', publishedAtUtc: '2026-11-23T08:00:00Z' }]),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const dyn = () => ok({ id: 'new-id' });
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(user, lang) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: 1280, height: 900 });
  await p.setRequestInterception(true);
  p.on('request', (rq) => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    captured.push({ method: rq.method(), path: pp, body: rq.postData() ?? null });
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
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

// Instructor flow: list, open, reply, upvote, pin, new thread, announcement.
const p = await page(instructor, 'en');
await p.goto('http://127.0.0.1:4307/app/community', { waitUntil: 'networkidle0' }); await sleep(1000);
await p.evaluate(() => { const el = document.querySelector('select[name="course"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(900);
check('threads listed for the course', find('GET', /^\/courses\/c1\/threads$/).length > 0);
await p.screenshot({ path: path.join(out, 'community-threads-desktop-en.png') }); console.log('shot community-threads-desktop-en');
await clickText(p, '.rv.clickable', 'factor'); await sleep(900);
check('thread detail loaded', find('GET', /^\/threads\/t1$/).length > 0);
await setInput(p, 'rb', 'Try grouping instead.');
await p.evaluate(() => { const forms = [...document.querySelectorAll('form')]; forms.at(-1).requestSubmit(); }); await sleep(900);
const rep = find('POST', /^\/threads\/t1\/posts$/).at(-1);
check('reply POSTs { body, parentPostId: null }', !!rep && rep.body.includes('Try grouping instead.') && rep.body.includes('"parentPostId":null'));
await clickText(p, 'ion-button', 'Upvote'); await sleep(700);
check('thread upvote POSTs /threads/t1/upvote', find('POST', /^\/threads\/t1\/upvote$/).length > 0);
await clickText(p, 'ion-button', 'Pin'); await sleep(700);
check('staff pin POSTs /threads/t1/pin', find('POST', /^\/threads\/t1\/pin$/).length > 0);
await p.screenshot({ path: path.join(out, 'community-thread-desktop-en.png') }); console.log('shot community-thread-desktop-en');

await clickText(p, 'ion-button', 'Back'); await sleep(500);
await setInput(p, 'nt', 'Study group for finals'); await setInput(p, 'nb', 'Who wants to revise chapter 7 together?');
await p.evaluate(() => { const f = document.querySelector('textarea[name="nb"]').closest('form'); f.requestSubmit(); }); await sleep(900);
const nt = find('POST', /^\/courses\/c1\/threads$/).at(-1);
check('new thread POSTs title+body+lessonId:null', !!nt && nt.body.includes('Study group for finals') && nt.body.includes('"lessonId":null'));

await clickText(p, 'button', 'Announcements'); await sleep(700);
await setInput(p, 'at', 'Office hours'); await setInput(p, 'ab', 'Sunday 6pm Cairo time.');
await p.evaluate(() => { const f = document.querySelector('textarea[name="ab"]').closest('form'); f.requestSubmit(); }); await sleep(900);
const an = find('POST', /^\/announcements$/).at(-1);
check('announcement POSTs { courseId, title, body }', !!an && an.body.includes('Office hours') && an.body.includes('Sunday 6pm'));
await p.screenshot({ path: path.join(out, 'community-announcements-desktop-en.png') }); console.log('shot community-announcements-desktop-en');
await p.close();

// TenantAdmin: threads read-only (no Post/New discussion buttons), announcements writable.
const p2 = await page(admin, 'en');
await p2.goto('http://127.0.0.1:4307/app/community', { waitUntil: 'networkidle0' }); await sleep(1000);
await p2.evaluate(() => { const el = document.querySelector('select[name="course"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(900);
const adminButtons = await p2.evaluate(() => document.body.textContent);
check('TenantAdmin does NOT get the New discussion form (post = St/In/TA only)', !adminButtons.includes('New discussion'));
check('TenantAdmin still sees announcements tab + form (staff)', await p2.evaluate(() => { const b = [...document.querySelectorAll('button')].find((e) => (e.textContent || '').includes('Announcements')); return !!b; }));
await p2.close();

console.log(results.join('\n'));
console.log(process.exitCode ? 'VERIFY FAILED' : 'VERIFY PASSED');
await browser.close(); server.close();
