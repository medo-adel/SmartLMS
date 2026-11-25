// Round 2 interaction verification: drives the real assignments UI in headless Chrome against
// contract-shaped fixtures (NOT the real backend) and asserts request payloads and confirmations:
//   1. Create assignment POSTs the AssignmentRequest contract (title, isPublished, rubric).
//   2. Rubric grading PUTs scores with criterionId + points to /submissions/{id}/grade.
//   3. Return-for-resubmission is two-tap confirmed; second tap POSTs feedback.
//   4. Assign peer reviews POSTs /assignments/{id}/peer-reviews/assign.
// Exits non-zero if any assertion fails.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-verify2'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4304);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const instructor = { id: 'u-in', tenantId: TID, fullName: 'Dr. Karim Nabil', email: 'karim@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: null };
const captured = [];
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'Published', ownerInstructorId: 'u-in' }]),
  'GET /courses/c1/assignments': ok([
    { id: 'a1', title: 'Problem set 4', instructions: 'Show your work.', dueAtUtc: null, maxPoints: 100, isGroup: false, maxGroupSize: null, allowResubmission: true, peerReviewEnabled: false, isPublished: true,
      rubric: [{ id: 'r1', name: 'Correctness', description: null, maxPoints: 70 }, { id: 'r2', name: 'Presentation', description: null, maxPoints: 30 }] },
    { id: 'a2', title: 'Group project', instructions: null, dueAtUtc: null, maxPoints: 50, isGroup: true, maxGroupSize: 4, allowResubmission: false, peerReviewEnabled: true, isPublished: false, rubric: [] } ]),
  'GET /assignments/a1/submissions': ok([
    { id: 's1', userId: 'u2', assignmentGroupId: null, version: 2, isLate: false, status: 'Submitted', totalScore: null, submittedAtUtc: '2026-11-25T09:12:00Z', studentName: 'Mona Adel' } ]),
  'GET /submissions/s1': ok({ id: 's1', version: 2, textContent: 'My worked solutions: 1) x=3 ...', linkUrl: 'https://example.com/work.pdf', isLate: false, submittedAtUtc: '2026-11-25T09:12:00Z', status: 'Submitted', totalScore: null, feedback: null, author: 'u2', comments: [{ id: 'cm1', body: 'Check question 2 again.', anchorJson: null, createdAtUtc: '2026-11-25T10:00:00Z' }], scores: [], maxPoints: 100 }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const dyn = (method, pp, body) => {
  if (method === 'POST' && pp === '/courses/c1/assignments') return ok({ id: 'a-new' });
  return ok({});
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: 1280, height: 900 });
await p.setRequestInterception(true);
p.on('request', (rq) => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  captured.push({ method: rq.method(), path: pp, body: rq.postData() ?? null });
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? dyn(rq.method(), pp, rq.postData())) });
});
await p.evaluateOnNewDocument((u) => {
  localStorage.setItem('CapacitorStorage.lang', 'en');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
}, instructor);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const find = (method, pathRe) => captured.filter((c) => c.method === method && pathRe.test(c.path));
const setInput = (name, value) => p.evaluate((n, v) => { const inp = document.querySelector(`input[name="${n}"], textarea[name="${n}"]`); inp.value = v; inp.dispatchEvent(new Event('input', { bubbles: true })); }, name, value);
const clickText = (sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim().includes(t)); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);
const clickInput = (name) => p.evaluate((n) => { const el = document.querySelector(`input[name="${n}"]`); el.click(); }, name);

await p.goto('http://127.0.0.1:4304/app/assignments', { waitUntil: 'networkidle0' }); await sleep(900);
await p.evaluate(() => { const el = document.querySelector('select[name="course"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(900);

// 1. Create assignment with rubric and Published checked.
await clickText('ion-button', 'New assignment'); await sleep(500);
await setInput('ti', 'Quiz 5 prep'); await setInput('ins', 'Chapters 7-9.');
await clickInput('ip'); await sleep(150); // Published
await clickText('ion-button', 'Add criterion'); await sleep(300);
await setInput('rn0', 'Effort'); await setInput('rp0', '10'); await sleep(150);
await p.evaluate(() => { const forms = [...document.querySelectorAll('form')]; forms.at(-1).requestSubmit(); }); await sleep(1000);
const created = find('POST', /^\/courses\/c1\/assignments$/).at(-1);
check('create assignment POSTs to /courses/c1/assignments', !!created);
check('create payload has title, isPublished true, rubric criterion', !!created && created.body.includes('Quiz 5 prep') && created.body.includes('"isPublished":true') && created.body.includes('Effort'));

// 2. Rubric grading.
await clickText('ion-button', 'Submissions'); await sleep(900);
await clickText('ion-button', 'Grade'); await sleep(900);
check('opening the grading desk loads the submission content (GET /submissions/s1)', find('GET', /^\/submissions\/s1$/).length > 0);
check('grader sees the submission text in the page', await p.evaluate(() => document.body.textContent.includes('My worked solutions')));
check('grader sees prior comments in the page', await p.evaluate(() => document.body.textContent.includes('Check question 2 again.')));
await setInput('scr1', '65'); await setInput('scr2', '25'); await setInput('fb', 'Good work.');
await p.evaluate(() => { const forms = [...document.querySelectorAll('form')]; forms.at(-1).requestSubmit(); }); await sleep(1000);
const graded = find('PUT', /^\/submissions\/s1\/grade$/).at(-1);
check('grade PUTs /submissions/s1/grade', !!graded);
check('grade payload has criterion scores 65 and 25 with feedback', !!graded && graded.body.includes('"criterionId":"r1","points":65') && graded.body.includes('"criterionId":"r2","points":25') && graded.body.includes('Good work.'));
await p.screenshot({ path: path.join(out, 'verify-grading.png') });

// 3. Return is two-tap confirmed.
await clickText('ion-button', 'Grade'); await sleep(500);
const before = find('POST', /\/return$/).length;
await clickText('ion-button', 'Return for resubmission'); await sleep(500);
check('first return tap sends no request', find('POST', /\/return$/).length === before);
await clickText('ion-button', 'Confirm?'); await sleep(900);
check('second tap POSTs /submissions/s1/return', find('POST', /^\/submissions\/s1\/return$/).length > 0);

// 4. Assign peer reviews.
await clickText('ion-button', 'Assign peer reviews'); await sleep(900);
check('assign peer reviews POSTs /assignments/a2/peer-reviews/assign', find('POST', /^\/assignments\/a2\/peer-reviews\/assign$/).length > 0);

// 5. TeachingAssistant session: grading endpoints visible, manage endpoints NOT (Mgr = TenantAdmin+Instructor only).
const ta = { ...instructor, id: 'u-ta', fullName: 'Omar TA', roles: ['TeachingAssistant'] };
const ctx2 = await browser.createBrowserContext(); const p2 = await ctx2.newPage(); await p2.setViewport({ width: 1280, height: 900 });
await p2.setRequestInterception(true);
p2.on('request', (rq) => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  captured.push({ method: rq.method(), path: pp, body: rq.postData() ?? null });
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? dyn(rq.method(), pp, rq.postData())) });
});
await p2.evaluateOnNewDocument((u) => {
  localStorage.setItem('CapacitorStorage.lang', 'en');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
}, ta);
await p2.goto('http://127.0.0.1:4304/app/assignments', { waitUntil: 'networkidle0' }); await sleep(900);
await p2.evaluate(() => { const el = document.querySelector('select[name="course"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(900);
const taButtons = await p2.evaluate(() => [...document.querySelectorAll('ion-button')].map((e) => (e.textContent || '').trim()).join(' | '));
check('TA sees the Submissions (grading) action', taButtons.includes('Submissions'));
check('TA does NOT see New assignment (Mgr-only)', !taButtons.includes('New assignment'));
check('TA does NOT see Edit (Mgr-only)', !taButtons.split('|').some((b) => b.trim() === 'Edit'));
check('TA does NOT see Assign peer reviews (Mgr-only)', !taButtons.includes('Assign peer reviews'));
await p2.evaluate(() => { const el = [...document.querySelectorAll('ion-button')].find((e) => (e.textContent || '').includes('Submissions')); el.click(); }); await sleep(900);
check('TA can load submissions (Grd endpoint)', find('GET', /^\/assignments\/a1\/submissions$/).length >= 2);

console.log(results.join('\n'));
console.log(process.exitCode ? 'VERIFY FAILED' : 'VERIFY PASSED');
await browser.close(); server.close();
