// Homework (student) screenshots. Serves dist/ with contract fixtures (NOT the real backend).
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-homework'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4306);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const student = { id: 'u2', tenantId: TID, fullName: 'Mona Adel', email: 'mona@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null };
const fx = {
  '/tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  '/me/enrollments': ok([{ id: 'e1', courseId: 'c1', title: 'Algebra II', status: 'Active', progressPercent: 40, expiresAtUtc: null }]),
  '/courses/c1/assignments': ok([
    { id: 'a1', title: 'Problem set 4', instructions: 'Show your work.', dueAtUtc: '2026-11-30T21:00:00Z', maxPoints: 100, isGroup: false, maxGroupSize: null, allowResubmission: true, peerReviewEnabled: false, isPublished: true, rubric: [] },
    { id: 'a2', title: 'Group project', instructions: 'Design a poster.', dueAtUtc: '2026-12-05T21:00:00Z', maxPoints: 50, isGroup: true, maxGroupSize: 4, allowResubmission: false, peerReviewEnabled: true, isPublished: true, rubric: [] } ]),
  '/peer-reviews/mine': ok([{ id: 'pr1', submissionId: 's9', status: 'Assigned', assignmentTitle: 'Essay draft', maxPoints: 30, rubric: [{ id: 'r1', name: 'Clarity', maxPoints: 30 }] }]),
  '/submissions/s9': ok({ id: 's9', version: 1, textContent: 'Peer essay text to review: the quadratic formula connects...', linkUrl: null, isLate: false, submittedAtUtc: '2026-11-24T10:00:00Z', status: null, totalScore: null, feedback: null, author: null, comments: [], scores: [], maxPoints: 30 }),
  '/me/devices': ok([]), '/notifications': ok([]),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(w, h, lang) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: w, height: h, deviceScaleFactor: 1 });
  await p.setRequestInterception(true);
  p.on('request', (rq) => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (fx[pp]) return rq.respond({ status: 200, headers: h, body: JSON.stringify(fx[pp]) });
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({})) });
  });
  await p.evaluateOnNewDocument((l, u) => {
    localStorage.setItem('CapacitorStorage.lang', l);
    localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
    localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
  }, lang, student);
  return p;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const clickText = (p, sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim().includes(t)); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);

let p = await page(1280, 900, 'en');
await p.goto('http://127.0.0.1:4306/app/homework', { waitUntil: 'networkidle0' }); await sleep(1000);
await p.evaluate(() => { const el = document.querySelector('select[name="course"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(900);
await p.screenshot({ path: path.join(out, 'homework-list-desktop-en.png') }); console.log('shot homework-list-desktop-en');
await clickText(p, 'ion-button', 'Review'); await sleep(900);
await p.screenshot({ path: path.join(out, 'homework-review-desktop-en.png') }); console.log('shot homework-review-desktop-en');
await p.close();
p = await page(390, 844, 'ar');
await p.goto('http://127.0.0.1:4306/app/homework', { waitUntil: 'networkidle0' }); await sleep(1000);
await p.evaluate(() => { const el = document.querySelector('select[name="course"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(900);
await clickText(p, 'ion-button', 'فتح'); await sleep(700);
await p.screenshot({ path: path.join(out, 'homework-submit-mobile-ar.png') }); console.log('shot homework-submit-mobile-ar');
console.log('DONE');
await browser.close(); server.close();
