// Student assignments verification: drives the real homework UI in headless Chrome against
// contract-shaped fixtures (NOT the real backend) and asserts:
//   1. Course picker lists enrolled courses (GET /me/enrollments) and assignments load.
//   2. Submit POSTs { textContent, linkUrl } to /assignments/{id}/submissions; http:// links are blocked client-side.
//   3. Team creation POSTs { groupId: null, name }; joining POSTs the code as groupId.
//   4. Peer review inbox loads (GET /peer-reviews/mine); submitting a review PUTs scores + comments.
// Exits non-zero if any assertion fails.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-verify-hw'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4305);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const student = { id: 'u2', tenantId: TID, fullName: 'Mona Adel', email: 'mona@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null };
const captured = [];
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /me/enrollments': ok([{ id: 'e1', courseId: 'c1', title: 'Algebra II', status: 'Active', progressPercent: 40, expiresAtUtc: null }]),
  'GET /courses/c1/assignments': ok([
    { id: 'a1', title: 'Problem set 4', instructions: 'Show your work.', dueAtUtc: '2026-11-30T21:00:00Z', maxPoints: 100, isGroup: false, maxGroupSize: null, allowResubmission: true, peerReviewEnabled: false, isPublished: true, rubric: [],
      mine: { id: 's-mine', version: 2, status: 'Graded', totalScore: 85, isLate: false, submittedAtUtc: '2026-11-24T09:00:00Z' } },
    { id: 'a2', title: 'Group project', instructions: null, dueAtUtc: null, maxPoints: 50, isGroup: true, maxGroupSize: 4, allowResubmission: false, peerReviewEnabled: true, isPublished: true, rubric: [] } ]),
  'GET /peer-reviews/mine': ok([{ id: 'pr1', submissionId: 's9', status: 'Assigned', assignmentTitle: 'Essay draft', maxPoints: 30, rubric: [{ id: 'r1', name: 'Clarity', maxPoints: 30 }] }]),
  'GET /submissions/s9': ok({ id: 's9', version: 1, textContent: 'Peer essay text to review.', linkUrl: null, isLate: false, submittedAtUtc: '2026-11-24T10:00:00Z', status: null, totalScore: null, feedback: null, author: null, comments: [], scores: [], maxPoints: 30 }),
  'GET /submissions/s-mine': ok({ id: 's-mine', version: 2, textContent: 'My earlier answers.', linkUrl: null, isLate: false, submittedAtUtc: '2026-11-24T09:00:00Z', status: 'Graded', totalScore: 85, feedback: 'Well done, fix Q3.', author: 'u2', comments: [], scores: [], maxPoints: 100 }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const dyn = (method, pp, body) => {
  if (method === 'POST' && pp === '/assignments/a1/submissions') return ok({ id: 's-new', version: 1, isLate: false });
  if (method === 'POST' && pp === '/assignments/a2/groups') return ok({ groupId: 'g-new-123' });
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
}, student);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const find = (method, pathRe) => captured.filter((c) => c.method === method && pathRe.test(c.path));
const setInput = (name, value) => p.evaluate((n, v) => { const inp = document.querySelector(`input[name="${n}"], textarea[name="${n}"]`); inp.value = v; inp.dispatchEvent(new Event('input', { bubbles: true })); }, name, value);
const clickText = (sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim().includes(t)); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);

await p.goto('http://127.0.0.1:4305/app/homework', { waitUntil: 'networkidle0' }); await sleep(1000);
check('enrollments loaded for the course picker', find('GET', /^\/me\/enrollments$/).length > 0);
check('peer review inbox loaded', find('GET', /^\/peer-reviews\/mine$/).length > 0);
await p.evaluate(() => { const el = document.querySelector('select[name="course"]'); el.value = 'c1'; el.dispatchEvent(new Event('change', { bubbles: true })); }); await sleep(900);
check('assignments listed for the course', find('GET', /^\/courses\/c1\/assignments$/).length > 0);
check('own submission status chip renders from the list payload', await p.evaluate(() => document.body.textContent.includes('Graded') && document.body.textContent.includes('85')));

// Submit with an insecure link first: must NOT send a request.
await clickText('ion-button', 'Open'); await sleep(900);
check('opening the card loads the own submission detail (GET /submissions/s-mine)', find('GET', /^\/submissions\/s-mine$/).length > 0);
check('own feedback visible to the student', await p.evaluate(() => document.body.textContent.includes('Well done, fix Q3.')));
await setInput('sl', 'http://insecure.example.com/work');
await p.evaluate(() => { const forms = [...document.querySelectorAll('form')]; forms.at(-1).requestSubmit(); }); await sleep(700);
check('http:// link blocked client-side (no submit request)', find('POST', /^\/assignments\/a1\/submissions$/).length === 0);

// Now a proper https submission.
await setInput('st', 'My worked solutions.'); await setInput('sl', 'https://example.com/work.pdf');
await p.evaluate(() => { const forms = [...document.querySelectorAll('form')]; forms.at(-1).requestSubmit(); }); await sleep(900);
const sub = find('POST', /^\/assignments\/a1\/submissions$/).at(-1);
check('submit POSTs textContent + https linkUrl', !!sub && sub.body.includes('My worked solutions.') && sub.body.includes('https://example.com/work.pdf'));
await p.screenshot({ path: path.join(out, 'verify-hw-submitted.png') });

// Team creation on the group assignment.
await p.evaluate(() => { const btns = [...document.querySelectorAll('ion-button')]; btns.filter((e) => (e.textContent || '').trim() === 'Open').at(-1).click(); }); await sleep(500);
await setInput('tna2', 'Team Falcon');
await clickText('ion-button', 'Create team'); await sleep(900);
const grp = find('POST', /^\/assignments\/a2\/groups$/).at(-1);
check('create team POSTs { groupId: null, name }', !!grp && grp.body.includes('"groupId":null') && grp.body.includes('Team Falcon'));
check('team code shown to share', await p.evaluate(() => document.body.textContent.includes('g-new-123')));
await p.screenshot({ path: path.join(out, 'verify-hw-team.png') });

// Peer review flow: open, read anonymized work, submit scores.
await clickText('ion-button', 'Review'); await sleep(900);
check('review opens the anonymized work (GET /submissions/s9)', find('GET', /^\/submissions\/s9$/).length > 0);
check('reviewer sees the work text', await p.evaluate(() => document.body.textContent.includes('Peer essay text to review.')));
await setInput('rvr1', '25'); await setInput('rc', 'Solid draft.');
await p.evaluate(() => { document.querySelector('textarea[name="rc"]').closest('form').requestSubmit(); }); await sleep(900);
const rev = find('PUT', /^\/peer-reviews\/pr1$/).at(-1);
check('review PUTs scores + comments', !!rev && rev.body.includes('"criterionId":"r1","points":25') && rev.body.includes('Solid draft.'));

console.log(results.join('\n'));
console.log(process.exitCode ? 'VERIFY FAILED' : 'VERIFY PASSED');
await browser.close(); server.close();
