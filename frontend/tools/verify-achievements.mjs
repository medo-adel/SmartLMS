// Achievements, parent area, approvals parent-links, player note-edit. Real UI in headless Chrome against contract fixtures.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-achievements'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4310);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const mk = (id, name, email, roles) => ({ id, tenantId: TID, fullName: name, email, phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles, lastLoginAtUtc: null });
const student = mk('u-st', 'Omar Khaled', 'omar@demo.test', ['Student']);
const parent = mk('u-pa', 'Khaled Mahmoud', 'khaled@demo.test', ['Parent']);
const admin = mk('u-admin', 'Hana Mostafa', 'admin@demo.test', ['TenantAdmin']);
const instructor = mk('u-in', 'Sara Adel', 'sara@demo.test', ['Instructor']);
const captured = [];
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /gamification/me': ok({ xp: 230, level: 3, currentStreakDays: 4, longestStreakDays: 9, badges: [{ code: 'first-course', name: 'First course', awardedAtUtc: '2026-11-01T10:00:00Z' }] }),
  'GET /gamification/leaderboard': ok([{ rank: 1, name: 'Mona A.', xp: 310, isMe: false }, { rank: 2, name: 'Omar K.', xp: 230, isMe: true }]),
  'GET /certificates/mine': ok([{ id: 'cert1', courseId: 'c1', courseTitle: 'Algebra II', issuedAtUtc: '2026-11-20T10:00:00Z', verificationCode: 'VC-1234-ABCD' }]),
  'GET /certificates/verify/VC-1234-ABCD': ok({ valid: true, holderName: 'Omar Khaled', course: 'Algebra II', issuedAtUtc: '2026-11-20T10:00:00Z', status: 'Valid' }),
  'POST /students/me/link-codes': ok({ code: 'PARENT-CODE-XYZ', expiresAtUtc: '2026-11-25T15:30:00Z' }),
  'GET /parent/children': ok([{ studentId: 'u-st', fullName: 'Omar Khaled' }]),
  'GET /parent/children/u-st/summary': ok({ studentId: 'u-st', courses: [{ title: 'Algebra II', status: 'Active', progressPercent: 42 }], averageExamScorePercent: 81.5, weeklyStudyHours: 3.5, attendancePercent: null, riskAlerts: ['LowScores'] }),
  'GET /parent/alerts': ok([{ id: 'al1', studentUserId: 'u-st', alertType: 'LowScores', message: 'Average exam score below 60% in the last graded exams.', createdAtUtc: '2026-11-24T09:00:00Z', readAtUtc: null }]),
  'POST /parent/links': ok({ linkId: 'pl1', status: 'Pending' }),
  'GET /parent/links/pending': ok([{ linkId: 'pl1', parentName: 'Khaled Mahmoud', studentName: 'Omar Khaled', createdAtUtc: '2026-11-25T10:00:00Z', status: 'Pending' }]),
  'GET /courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'Published', ownerInstructorId: 'u-in' }]),
  'GET /users': ok({ items: [{ id: 'u-st', tenantId: TID, fullName: 'Omar Khaled', email: 'omar@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null }], totalCount: 1, page: 1, pageSize: 20 }),
  'POST /certificates/issue': ok({ id: 'cert-new', verificationCode: 'VC-NEW-9999' }),
  'GET /courses': ok({ items: [], total: 0 }),
  'GET /courses/c1': ok({ id: 'c1', title: 'Algebra II', slug: 'algebra-ii', description: null, status: 'Published', sections: [{ id: 's1', title: 'Basics', lessons: [{ id: 'l1', title: 'Intro to factors', lessonType: 'Text', durationSeconds: null, isFreePreview: false }] }], price: 0, currency: 'EGP', pricingModel: 'Free', coverImageUrl: null, averageRating: null, ratingsCount: 0, enrolled: true, ownerInstructorId: 'u-in' }),
  'GET /lessons/l1': ok({ id: 'l1', courseId: 'c1', sectionId: 's1', title: 'Intro to factors', lessonType: 'Text', contentMarkdown: 'Factor means multiply.', externalUrl: null, durationSeconds: null, isFreePreview: false }),
  'GET /lessons/l1/notes': ok([{ id: 'n1', timestampSeconds: null, body: 'Remember difference of squares.', createdAtUtc: '2026-11-24T12:00:00Z' }]),
  'GET /lessons/l1/bookmarks': ok([]),
  'GET /lessons/l1/chapters': ok([]),
  'GET /lessons/l1/resources': ok([]),
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
    captured.push({ method: rq.method(), path: pp, query: new URL(rq.url()).search, body: rq.postData() ?? null, auth: rq.headers()['authorization'] ?? null });
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (pp === '/certificates/cert1/pdf') return rq.respond({ status: 200, headers: { ...h, 'content-type': 'application/pdf' }, body: Buffer.from('%PDF-1.4 fixture') });
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
const bodyText = (p) => p.evaluate(() => document.body.innerText);

// ---- student achievements ----
const p = await page(student, 'en');
await p.goto('http://127.0.0.1:4310/app/achievements', { waitUntil: 'networkidle0' }); await sleep(1000);
const t1 = await bodyText(p);
check('student XP/level/streak rendered', t1.includes('230') && t1.includes('Level 3') && t1.includes('First course'));
check('leaderboard marks the caller', t1.includes('(you)') && t1.includes('Mona A.'));
check('certificate row + code rendered', t1.includes('Algebra II') && t1.includes('VC-1234-ABCD'));
await clickText(p, 'ion-button', 'Download PDF'); await sleep(800);
const pdfReq = find('GET', /^\/certificates\/cert1\/pdf$/).at(-1);
check('certificate PDF fetched through the authenticated client (bearer header present)', !!pdfReq && !!pdfReq.auth);
await setInput(p, 'verifyCode', 'VC-1234-ABCD');
await p.evaluate(() => { document.querySelector('input[name="verifyCode"]').closest('form').requestSubmit(); }); await sleep(700);
check('verify shows Valid result', (await bodyText(p)).includes('Valid') && (await bodyText(p)).includes('Omar Khaled'));
await clickText(p, 'ion-button', 'Generate link code'); await sleep(700);
check('link code generated and shown with expiry', (await bodyText(p)).includes('PARENT-CODE-XYZ') && find('POST', /^\/students\/me\/link-codes$/).length > 0);
await p.screenshot({ path: path.join(out, 'achievements-student-en.png') }); console.log('shot achievements-student-en');

// ---- parent area ----
const p2 = await page(parent, 'en');
await p2.goto('http://127.0.0.1:4310/app/parent', { waitUntil: 'networkidle0' }); await sleep(1100);
const t2 = await bodyText(p2);
check('child summary rendered (hours, score, course, risk tag)', t2.includes('3.5') && t2.includes('81.5') && t2.includes('Algebra II') && t2.includes('LowScores'));
check('alert rendered', t2.includes('Average exam score below 60%'));
await setInput(p2, 'linkCode', 'PARENT-CODE-XYZ');
await p2.evaluate(() => { document.querySelector('input[name="linkCode"]').closest('form').requestSubmit(); }); await sleep(800);
const rl = find('POST', /^\/parent\/links$/).at(-1);
check('link request POSTs { code } and shows pending state', !!rl && rl.body.includes('PARENT-CODE-XYZ') && (await bodyText(p2)).includes('Waiting for academy approval'));
await p2.screenshot({ path: path.join(out, 'parent-child-summary-en.png') }); console.log('shot parent-child-summary-en');

// ---- admin approvals with pending links endpoint live ----
const p3 = await page(admin, 'en');
await p3.goto('http://127.0.0.1:4310/app/approvals', { waitUntil: 'networkidle0' }); await sleep(1100);
check('pending link row rendered', (await bodyText(p3)).includes('Khaled Mahmoud') && (await bodyText(p3)).includes('Omar Khaled'));
await p3.screenshot({ path: path.join(out, 'approvals-parent-links-pending-en.png') }); console.log('shot approvals-parent-links-pending-en');
await clickText(p3, 'ion-button', 'Approve'); await sleep(800);
const sl = find('PUT', /^\/parent\/links\/pl1$/).at(-1);
check('approve PUTs { approve: true } and removes the row', !!sl && sl.body.includes('"approve":true') && !(await bodyText(p3)).includes('Khaled Mahmoud'));
await p3.screenshot({ path: path.join(out, 'approvals-parent-links-en.png') }); console.log('shot approvals-parent-links-en');

// ---- player note edit ----
const p4 = await page(student, 'en');
await p4.goto('http://127.0.0.1:4310/app/learn/c1/l1', { waitUntil: 'networkidle0' }); await sleep(1200);
check('note listed in player extras', (await bodyText(p4)).includes('Remember difference of squares'));
await clickText(p4, 'ion-button', 'Edit'); await sleep(500);
await setInput(p4, 'edit-nb', 'Difference of squares: a^2 - b^2 = (a-b)(a+b).');
await p4.screenshot({ path: path.join(out, 'player-note-editing-en.png') }); console.log('shot player-note-editing-en');
await p4.evaluate(() => { const el = [...document.querySelectorAll('ion-button')].find((e) => (e.textContent || '').trim() === 'Save'); if (!el) throw new Error('save button not found'); el.click(); }); await sleep(800);
const en = find('PUT', /^\/notes\/n1$/).at(-1);
check('note edit PUTs { timestampSeconds, body }', !!en && en.body.includes('a^2 - b^2') && en.body.includes('"timestampSeconds":null'));
await p4.screenshot({ path: path.join(out, 'player-note-edit-en.png') }); console.log('shot player-note-edit-en');

// ---- admin issues a certificate with a resolved student picker ----
const pa = await page(admin, 'en');
await pa.goto('http://127.0.0.1:4310/app/achievements', { waitUntil: 'networkidle0' }); await sleep(1400);
let ta = await bodyText(pa);
check('manager: issue section with course picker rendered', ta.includes('Issue a certificate') && ta.includes('Algebra II'));
await pa.evaluate(() => { const sel = document.querySelector('select[name="issueCourseId"]'); sel.value = 'c1'; sel.dispatchEvent(new Event('change', { bubbles: true })); });
await setInput(pa, 'studentQuery', 'omar');
await pa.evaluate(() => { document.querySelector('input[name="studentQuery"]').closest('form').requestSubmit(); }); await sleep(900);
ta = await bodyText(pa);
check('student search resolves a pickable student', ta.includes('Omar Khaled') && ta.includes('omar@demo.test'));
const usersReq = find('GET', /^\/users$/).at(-1);
check('student search hit GET /users with role=Student', !!usersReq && usersReq.query?.includes('role=Student'));
await clickText(pa, 'button', 'Omar Khaled'); await sleep(400);
ta = await bodyText(pa);
check('picked student shown with identity before issuing', ta.includes('Omar Khaled'));
await clickText(pa, 'ion-button', 'Issue certificate'); await sleep(900);
const iss = find('POST', /^\/certificates\/issue$/).at(-1);
check('issue POST payload {userId, courseId}', !!iss && iss.body && (() => { const b = JSON.parse(iss.body); return b.userId === 'u-st' && b.courseId === 'c1'; })());
ta = await bodyText(pa);
check('issued result shows the verification code', ta.includes('Certificate issued.') && ta.includes('VC-NEW-9999'));
await pa.screenshot({ path: path.join(out, 'achievements-issue-en.png') }); console.log('shot achievements-issue-en');
await pa.close();

// ---- instructor session issues for her own course (GET /users Viewers = SuperAdmin,TenantAdmin,Instructor per UsersController.cs:13) ----
const pin = await page(instructor, 'en');
await pin.goto('http://127.0.0.1:4310/app/achievements', { waitUntil: 'networkidle0' }); await sleep(1400);
let ti = await bodyText(pin);
check('instructor: issue section rendered', ti.includes('Issue a certificate') && ti.includes('Algebra II'));
await pin.evaluate(() => { const sel = document.querySelector('select[name="issueCourseId"]'); sel.value = 'c1'; sel.dispatchEvent(new Event('change', { bubbles: true })); });
await setInput(pin, 'studentQuery', 'omar');
await pin.evaluate(() => { document.querySelector('input[name="studentQuery"]').closest('form').requestSubmit(); }); await sleep(900);
await clickText(pin, 'button', 'Omar Khaled'); await sleep(300);
await clickText(pin, 'ion-button', 'Issue certificate'); await sleep(900);
const iss2 = find('POST', /^\/certificates\/issue$/).at(-1);
check('instructor: issue POST payload {userId, courseId}', !!iss2 && iss2.body && (() => { const b = JSON.parse(iss2.body); return b.userId === 'u-st' && b.courseId === 'c1'; })());
check('instructor: verification code shown', (await bodyText(pin)).includes('VC-NEW-9999'));
await pin.close();

console.log('\n' + results.join('\n'));
console.log(process.exitCode ? 'VERIFY FAILED' : 'VERIFY PASSED');
await browser.close(); server.close();
