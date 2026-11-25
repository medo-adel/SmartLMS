// Round 2 screenshot harness. Serves dist/ and answers API calls from contract-shaped fixtures (NOT the real backend).
// Usage: npx ng build --configuration development && node tools/shots-round2.mjs <outDir>
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-round2'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4303);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const instructor = { id: 'u-in', tenantId: TID, fullName: 'Dr. Karim Nabil', email: 'karim@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null };
const fx = {
  '/tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  '/courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'Published', ownerInstructorId: 'u-in' }]),
  '/courses/c1/assignments': ok([
    { id: 'a1', title: 'Problem set 4', instructions: 'Show your work.', dueAtUtc: '2026-11-30T21:00:00Z', maxPoints: 100, isGroup: false, maxGroupSize: null, allowResubmission: true, peerReviewEnabled: false, isPublished: true,
      rubric: [{ id: 'r1', name: 'Correctness', description: 'Right answers', maxPoints: 70 }, { id: 'r2', name: 'Presentation', description: 'Clear steps', maxPoints: 30 }] },
    { id: 'a2', title: 'Group project', instructions: null, dueAtUtc: null, maxPoints: 50, isGroup: true, maxGroupSize: 4, allowResubmission: false, peerReviewEnabled: true, isPublished: false, rubric: [] } ]),
  '/assignments/a1/submissions': ok([
    { id: 's1', userId: 'u2', assignmentGroupId: null, version: 2, isLate: false, status: 'Submitted', totalScore: null, submittedAtUtc: '2026-11-25T09:12:00Z', studentName: 'Mona Adel' },
    { id: 's2', userId: 'u4', assignmentGroupId: null, version: 1, isLate: true, status: 'Graded', totalScore: 82, submittedAtUtc: '2026-11-24T22:40:00Z', studentName: 'Tarek Samy' } ]),
  '/submissions/s1': ok({ id: 's1', version: 2, textContent: 'My worked solutions:\n1) x = 3, y = -2\n2) vertex at (1, -4)', linkUrl: 'https://example.com/work.pdf', isLate: false, submittedAtUtc: '2026-11-25T09:12:00Z', status: 'Submitted', totalScore: null, feedback: null, author: 'u2', comments: [{ id: 'cm1', body: 'Check question 2 again.', anchorJson: null, createdAtUtc: '2026-11-25T10:00:00Z' }], scores: [], maxPoints: 100 }),
  '/me/devices': ok([]), '/notifications': ok([]),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(w, h, lang, dark) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: w, height: h, deviceScaleFactor: 1 });
  await p.emulateMediaFeatures([{ name: 'prefers-color-scheme', value: dark ? 'dark' : 'light' }]);
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
  }, lang, instructor);
  return p;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const setSel = (p, name, value) => p.evaluate((n, v) => { const el = document.querySelector(`select[name="${n}"]`); el.value = v; el.dispatchEvent(new Event('change', { bubbles: true })); }, name, value);
const clickText = (p, sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim().includes(t)); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);

async function shots(lang, suffix, w, h, dark, labels) {
  const p = await page(w, h, lang, dark);
  await p.goto('http://127.0.0.1:4303/app/assignments', { waitUntil: 'networkidle0' }); await sleep(900);
  await setSel(p, 'course', 'c1'); await sleep(900);
  await p.screenshot({ path: path.join(out, `assignments-list-${suffix}.png`) }); console.log('shot', `assignments-list-${suffix}`);
  await clickText(p, 'ion-button', labels.new); await sleep(600);
  await p.screenshot({ path: path.join(out, `assignments-editor-${suffix}.png`) }); console.log('shot', `assignments-editor-${suffix}`);
  await clickText(p, 'ion-button', labels.cancel); await sleep(400);
  await clickText(p, 'ion-button', labels.subs); await sleep(900);
  await p.screenshot({ path: path.join(out, `assignments-subs-${suffix}.png`) }); console.log('shot', `assignments-subs-${suffix}`);
  await clickText(p, 'ion-button', labels.grade); await sleep(600);
  await p.screenshot({ path: path.join(out, `assignments-grading-${suffix}.png`) }); console.log('shot', `assignments-grading-${suffix}`);
  await p.close();
}
await shots('en', 'desktop-en', 1280, 900, false, { new: 'New assignment', cancel: 'Cancel', subs: 'Submissions', grade: 'Grade' });
await shots('ar', 'mobile-ar', 390, 844, false, { new: 'واجب جديد', cancel: 'إلغاء', subs: 'التسليمات', grade: 'تقييم' });
console.log('DONE');
await browser.close(); server.close();
