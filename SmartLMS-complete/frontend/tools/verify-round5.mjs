// Round 5: exam edit, manual grading, question import, course staff, prerequisites,
// share override, subscription controls, learning weight, webhooks, order detail, OTP resend.
// Role-specific sessions: instructor (authoring + builder), TenantAdmin (finance + ops), student (orders), anonymous (register).
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-round5'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4313);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const mk = (id, name, email, roles) => ({ id, tenantId: TID, fullName: name, email, phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles, lastLoginAtUtc: null });
const instructor = mk('u-in', 'Sara Adel', 'sara@demo.test', ['Instructor']);
const admin = mk('u-admin', 'Hana Mostafa', 'admin@demo.test', ['TenantAdmin']);
const student = mk('u-st', 'Omar Khaled', 'omar@demo.test', ['Student']);
const course1 = { id: 'c1', title: 'Algebra II', status: 'Draft', ownerInstructorId: 'u-in' };
const course2 = { id: 'c2', title: 'Physics Basics', status: 'Published', ownerInstructorId: 'u-in' };
const captured = [];
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /courses/mine': ok([course1, course2]),
  'GET /courses/c1': ok({ id: 'c1', title: 'Algebra II', slug: 'algebra-ii', shortDescription: '', description: null, status: 'Draft', level: 'Beginner', language: 'en', estimatedHours: 10, pricingModel: 'Free', price: null, currency: 'EGP', ownerInstructorId: 'u-in', sections: [{ id: 's1', title: 'Basics', description: null, sortOrder: 0, lessons: [{ id: 'l1', title: 'Intro', lessonType: 'Video', durationSeconds: 600, sortOrder: 0, isFreePreview: false }] }] }),
  'GET /courses/c1/question-banks': ok([{ id: 'qb1', name: 'Algebra bank', topic: null, questionCount: 2 }]),
  'GET /question-banks/qb1/questions': ok([{ id: 'q1', questionType: 'McqSingle', text: '1+1?', defaultPoints: 1, difficulty: 'Easy', topic: null, explanation: null, options: [] }]),
  'GET /courses/c1/exams': ok([{ id: 'e1', title: 'Quiz 1', kind: 'Quiz', durationMinutes: 20, maxAttempts: 2, passingScorePercent: 60, isPublished: false, availableFromUtc: null, availableUntilUtc: null }]),
  'GET /exams/e1': ok({ id: 'e1', courseId: 'c1', title: 'Quiz 1', kind: 'Quiz', instructions: 'Show your work', durationMinutes: 20, maxAttempts: 2, passingScorePercent: 60, availableFromUtc: '2026-12-01T09:00:00Z', availableUntilUtc: null, tabSwitchDetection: true, maxTabSwitches: 3, isPublished: false, locked: false }),
  'GET /exams/e1/attempts': ok([{ id: 'at1', userId: 'u-st', attemptNumber: 1, status: 'Submitted', score: null, maxScore: 10, passed: null, tabSwitchCount: 0 }]),
  'GET /users/u-st': ok(student),
  'GET /attempts/at1/result': ok({ attemptId: 'at1', userId: 'u-st', status: 'Submitted', score: null, maxScore: 10, passed: null, awaitingManualGrading: 2, tabSwitchCount: 0, answers: [{ answerId: 'ans1', questionId: 'q2', answerJson: { text: 'because x=2' }, isCorrect: null, pointsAwarded: null, feedback: null }, { answerId: 'ans2', questionId: 'q3', answerJson: { optionId: 'opt-7' }, isCorrect: null, pointsAwarded: null, feedback: null }] }),
  'GET /courses/c1/staff': ok([{ userId: 'u-ta', fullName: 'Mona Adel', staffRole: 'TeachingAssistant' }]),
  'GET /courses/c1/prerequisites': ok([{ courseId: 'c2', title: 'Physics Basics' }]),
  'GET /users': ok({ items: [mk('u-ta2', 'Kareem Fathy', 'kareem@demo.test', ['TeachingAssistant']), mk('u-in2', 'Laila Samir', 'laila@demo.test', ['Instructor'])], totalCount: 2, page: 1, pageSize: 20 }),
  'GET /dashboards/admin': ok({ kpis: { users: 1, publishedCourses: 1, activeEnrollments: 1, revenueLast30Days: 0, revenueAllTime: 0, paidOrdersLast30Days: 0 }, queues: { coursesAwaitingApproval: 0, manualPaymentsToReview: 0, payoutsToApprove: 0, failedWebhookDeliveries: 1 }, revenueByDay: [] }),
  'GET /instructor-revenue/shares': ok({ defaultInstructorSharePercent: 70, overrides: [] }),
  'GET /instructor-debits': ok({ items: [], outstanding: 0 }),
  'GET /instructor-payouts': ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
  'GET /instructor-revenue/subscription-controls': ok({ minWatchPercent: 60, minAverageLessonSeconds: 120, jumpAlertPercent: 40 }),
  'GET /courses/c1/learning-weight': ok({ courseId: 'c1', nonVideoCourseSeconds: 1800 }),
  'GET /webhooks': ok([{ id: 'wh1', url: 'https://hooks.example.com/in', eventTypes: ['order.paid'], isActive: true, consecutiveFailures: 1, createdAtUtc: '2026-11-01T00:00:00Z' }]),
  'GET /webhooks/wh1/deliveries': ok({ items: [{ id: 'dl1', eventType: 'order.paid', status: 'Failed', attemptCount: 3, lastHttpStatus: 500, lastError: 'Internal Server Error', nextAttemptAtUtc: null, deliveredAtUtc: null, createdAtUtc: '2026-11-20T10:00:00Z' }], page: 1, pageSize: 20, totalCount: 1 }),
  'GET /audit-logs': ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
  'GET /recycle-bin': ok({ items: [], supportedEntities: [] }),
  'GET /api-keys': ok([]),
  'GET /orders': ok({ items: [{ id: 'o1', status: 'Paid', subtotal: 500, discountTotal: 50, total: 450, currency: 'EGP', paymentMethod: 'Manual', createdAtUtc: '2026-11-10T10:00:00Z', paidAtUtc: '2026-11-11T09:00:00Z' }], total: 1 }),
  'GET /orders/o1': ok({ id: 'o1', userId: 'u-st', status: 'Paid', subtotal: 500, discountTotal: 50, total: 450, currency: 'EGP', paymentMethod: 'Manual', createdAtUtc: '2026-11-10T10:00:00Z', paidAtUtc: '2026-11-11T09:00:00Z', items: [{ id: 'oi1', itemType: 'Course', description: 'Algebra II', unitPrice: 500, quantity: 1, lineTotal: 500 }], payments: [{ id: 'p1', method: 'Manual', status: 'Confirmed', amount: 450, currency: 'EGP', createdAtUtc: '2026-11-10T10:05:00Z', confirmedAtUtc: '2026-11-11T09:00:00Z', failureReason: null }], invoiceNumber: 'INV-2026-0042' }),
  'GET /affiliate/me': Object.assign(ok(null), { success: false, message: 'NOT_AN_AFFILIATE', data: null }),
  'GET /subscriptions/mine': ok([]),
};
const dyn = () => ok({ id: 'new-id' });
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(user, lang, notFound = []) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: 1280, height: 900 });
  await p.setRequestInterception(true);
  p.on('request', (rq) => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    captured.push({ method: rq.method(), path: pp, query: u.search, body: rq.postData() ?? null });
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (notFound.includes(rq.method() + ' ' + pp)) return rq.respond({ status: 404, headers: h, body: JSON.stringify({ success: false, data: null, message: 'NOT_FOUND', errors: null }) });
    if (rq.method() === 'POST' && pp === '/webhooks') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ id: 'wh2', url: 'https://hooks.example.com/new', eventTypes: ['order.paid'], isActive: true, secret: 'whsec_fixturesecret', note: 'Store the secret now. It is not shown again.' })) });
    const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? dyn()) });
  });
  await p.evaluateOnNewDocument((l, u) => {
    localStorage.setItem('CapacitorStorage.lang', l);
    localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
    if (u) localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
  }, lang, user);
  return p;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const find = (method, pathRe) => captured.filter((c) => c.method === method && pathRe.test(c.path));
const setInput = (p, name, value) => p.evaluate((n, v) => { const inp = document.querySelector(`input[name="${n}"], textarea[name="${n}"], select[name="${n}"]`); if (!inp) throw new Error('no input ' + n); inp.value = v; inp.dispatchEvent(new Event('input', { bubbles: true })); inp.dispatchEvent(new Event('change', { bubbles: true })); }, name, value);
const clickText = (p, sel, text) => p.evaluate((s, t) => { const el = [...document.querySelectorAll(s)].find((e) => (e.textContent || '').trim() === t); if (!el) throw new Error('not found: ' + t); el.click(); }, sel, text);
const bodyText = (p) => p.evaluate(() => document.body.innerText);
const overflow = (p) => p.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
const scrollToText = (p, sel, text) => p.evaluate((s2, t2) => { const el = [...document.querySelectorAll(s2)].find((e) => (e.textContent || '').includes(t2)); if (el) el.scrollIntoView({ block: 'start', behavior: 'instant' }); }, sel, text);

// ================= instructor: exam edit =================
{
  const p = await page(instructor, 'en');
  await p.goto('http://127.0.0.1:4313/app/assessments', { waitUntil: 'networkidle0' }); await sleep(1200);
  await setInput(p, 'course', 'c1'); await sleep(1200);
  let t = await bodyText(p);
  check('authoring: exam row listed', t.includes('Quiz 1'));
  await clickText(p, 'ion-button', 'Edit'); await sleep(900);
  t = await bodyText(p);
  check('exam edit prefilled from GET /exams/e1 (title, instructions, window)', t.includes('Edit exam') && (await p.evaluate(() => document.querySelector('input[name="eet"]').value)) === 'Quiz 1' && (await p.evaluate(() => document.querySelector('textarea[name="eei"]').value)) === 'Show your work' && (await p.evaluate(() => document.querySelector('input[name="eef"]').value)) === '2026-12-01T09:00');
  await setInput(p, 'eet', 'Quiz 1 (revised)');
  await p.evaluate(() => { document.querySelector('input[name="eet"]').closest('form').requestSubmit(); }); await sleep(900);
  const eu = find('PUT', /^\/exams\/e1$/).at(-1);
  check('exam update PUT sends full settings, instructions preserved', !!eu && !!eu.body && (() => { const b = JSON.parse(eu.body); return b.title === 'Quiz 1 (revised)' && b.instructions === 'Show your work' && b.durationMinutes === 20 && b.maxAttempts === 2 && b.passingScorePercent === 60 && b.tabSwitchDetection === true && b.maxTabSwitches === 3 && b.availableFromUtc !== null; })());
  check('exam list refetched after save', find('GET', /^\/courses\/c1\/exams$/).length >= 2);

  // ---- grading ----
  await setInput(p, 'gx', 'e1'); await sleep(1000);
  t = await bodyText(p);
  check('grading: attempt listed with resolved student name', t.includes('Omar Khaled') && t.includes('Submitted'));
  await clickText(p, 'ion-button', 'Review'); await sleep(900);
  t = await bodyText(p);
  check('grading: answer shown, awaiting tag', t.includes('because x=2') && t.includes('Awaiting grading'));
  check('text answer rendered as readable text, not JSON', !t.includes('"text"'));
  check('selected-option answer rendered with labeled fallback', t.includes('Selected option: opt-7'));
  await setInput(p, 'gpans1', '8'); await setInput(p, 'gfans1', 'Good reasoning');
  check('points and feedback inputs stay independent', (await p.evaluate(() => document.querySelector('input[name="gpans1"]').value)) === '8' && (await p.evaluate(() => document.querySelector('input[name="gfans1"]').value)) === 'Good reasoning');
  await scrollToText(p, 'h3', 'Attempt'); await sleep(300);
  await p.screenshot({ path: path.join(out, 'authoring-grading-en.png') }); console.log('shot authoring-grading-en');
  await clickText(p, 'ion-button', 'Save grade'); await sleep(900);
  const g = find('PUT', /^\/attempt-answers\/ans1\/grade$/).at(-1);
  check('grade PUT {points, feedback}', !!g && !!g.body && (() => { const b = JSON.parse(g.body); return b.points === 8 && b.feedback === 'Good reasoning'; })());
  check('grading view refetched after save', find('GET', /^\/attempts\/at1\/result$/).length >= 2);

  // ---- import ----
  await setInput(p, 'bank', 'qb1'); await sleep(900);
  await setInput(p, 'ict', '[{"questionType":"TrueFalse","text":"Sky is blue","points":1,"trueFalseAnswer":true}]');
  await p.evaluate(() => { document.querySelector('textarea[name="ict"]').closest('form').requestSubmit(); }); await sleep(900);
  const im = find('POST', /^\/question-banks\/qb1\/import$/).at(-1);
  check('import POST {format, content}', !!im && !!im.body && (() => { const b = JSON.parse(im.body); return b.format === 'json' && b.content.includes('Sky is blue'); })());
  t = await bodyText(p);
  check('imported count shown', t.includes('Imported questions'));

  // ---- option text vs checkbox independence regression (dynamic-name NgModel) ----
  await setInput(p, 'qx', '2+2?'); await setInput(p, 'ot0', '2'); await setInput(p, 'ot1', '4');
  await p.evaluate(() => { document.querySelector('input[name="oc1"]').click(); }); await sleep(300);
  await p.evaluate(() => { document.querySelector('input[name="ot0"]').closest('form').requestSubmit(); }); await sleep(900);
  const nq = find('POST', /^\/question-banks\/qb1\/questions$/).at(-1);
  check('question POST keeps option texts and correct flags independent', !!nq && !!nq.body && (() => { const b = JSON.parse(nq.body); const o = b.options ?? []; return o[0]?.text === '2' && o[0]?.isCorrect === false && o[1]?.text === '4' && o[1]?.isCorrect === true; })());
  check('authoring: no horizontal overflow', (await overflow(p)) === 0);
  await p.close();
}

// ================= instructor: staff + prerequisites =================
{
  const p = await page(instructor, 'en');
  await p.goto('http://127.0.0.1:4313/app/build', { waitUntil: 'networkidle0' }); await sleep(1200);
  await clickText(p, 'button', 'Algebra II (Draft)'); await sleep(1500);
  let t = await bodyText(p);
  check('staff listed with role', t.includes('Mona Adel') && t.includes('TeachingAssistant'));
  check('prerequisites prefilled from GET', t.includes('Prerequisites') && (await p.evaluate(() => [...document.querySelectorAll('input[type="checkbox"]')].some((c) => c.name === 'pqc2' && c.checked))));
  await setInput(p, 'ss', 'kareem'); await sleep(900);
  await clickText(p, 'ion-button', 'Add'); await sleep(400);
  await clickText(p, 'ion-button', 'Save staff'); await sleep(400);
  t = await bodyText(p);
  check('staff confirm strip shows resulting list before sending', t.includes('Confirm staff change') && t.includes('Kareem Fathy') && t.includes('Mona Adel') && find('PUT', /^\/courses\/c1\/staff$/).length === 0);
  await p.screenshot({ path: path.join(out, 'builder-staff-confirm-en.png') }); console.log('shot builder-staff-confirm-en');
  await clickText(p, 'ion-button', 'Confirm and save'); await sleep(900);
  const st = find('PUT', /^\/courses\/c1\/staff$/).at(-1);
  check('staff PUT replaces list with both members', !!st && !!st.body && (() => { const b = JSON.parse(st.body); return b.staff.length === 2 && b.staff.some((x) => x.userId === 'u-ta' && x.staffRole === 'TeachingAssistant') && b.staff.some((x) => x.userId === 'u-ta2'); })());
  check('staff refetched after save', find('GET', /^\/courses\/c1\/staff$/).length >= 2);
  // prerequisites: uncheck c2, save
  await p.evaluate(() => { const c = document.querySelector('input[name="pqc2"]'); c.click(); }); await sleep(300);
  await clickText(p, 'ion-button', 'Save prerequisites'); await sleep(400);
  t = await bodyText(p);
  check('prereq confirm strip states consequence', t.includes('Confirm prerequisites') && t.includes('No prerequisites'));
  await clickText(p, 'ion-button', 'Confirm and save'); await sleep(900);
  const pq = find('PUT', /^\/courses\/c1\/prerequisites$/).at(-1);
  check('prereq PUT {courseIds: []}', !!pq && !!pq.body && JSON.parse(pq.body).courseIds.length === 0);
  check('builder: no horizontal overflow', (await overflow(p)) === 0);
  await p.close();
}

// ================= TenantAdmin: finance override + controls + weight =================
{
  const p = await page(admin, 'en');
  await p.goto('http://127.0.0.1:4313/app/finance', { waitUntil: 'networkidle0' }); await sleep(1500);
  await clickText(p, 'button', 'Shares'); await sleep(900);
  await setInput(p, 'ovc', 'c1'); await setInput(p, 'ovi', 'u-in2'); await setInput(p, 'ovp', '55');
  await p.evaluate(() => { document.querySelector('select[name="ovc"]').closest('form').requestSubmit(); }); await sleep(400);
  let t = await bodyText(p);
  check('override confirm strip shows course, instructor, percent', t.includes('Confirm instructor share') && t.includes('Algebra II') && t.includes('Laila Samir') && t.includes('55%') && find('PUT', /^\/courses\/c1\/instructor-share$/).length === 0);
  await scrollToText(p, '.pn.confirm', 'Confirm instructor share'); await sleep(300);
  await p.screenshot({ path: path.join(out, 'finance-override-confirm-en.png') }); console.log('shot finance-override-confirm-en');
  await clickText(p, 'ion-button', 'Confirm and save'); await sleep(900);
  const ov = find('PUT', /^\/courses\/c1\/instructor-share$/).at(-1);
  check('override PUT {instructorId, percent}', !!ov && !!ov.body && (() => { const b = JSON.parse(ov.body); return b.instructorId === 'u-in2' && b.percent === 55; })());
  check('shares refetched after save', find('GET', /^\/instructor-revenue\/shares$/).length >= 2);

  await clickText(p, 'button', 'Controls'); await sleep(900);
  t = await bodyText(p);
  check('controls prefilled from GET', (await p.evaluate(() => document.querySelector('input[name="cmw"]').value)) === '60');
  await setInput(p, 'cmw', '75');
  await p.evaluate(() => { document.querySelector('input[name="cmw"]').closest('form').requestSubmit(); }); await sleep(400);
  t = await bodyText(p);
  check('controls confirm strip shows new values', t.includes('Confirm subscription controls') && t.includes('75') && find('PUT', /^\/instructor-revenue\/subscription-controls$/).length === 0);
  await clickText(p, 'ion-button', 'Confirm and save'); await sleep(900);
  const sc = find('PUT', /^\/instructor-revenue\/subscription-controls$/).at(-1);
  check('controls PUT {minWatchPercent: 75, ...}', !!sc && !!sc.body && (() => { const b = JSON.parse(sc.body); return b.minWatchPercent === 75 && b.minAverageLessonSeconds === 120 && b.jumpAlertPercent === 40; })());

  await setInput(p, 'lwc', 'c1'); await sleep(900);
  t = await bodyText(p);
  check('learning weight current shown (30 minutes)', t.includes('Current') && t.includes('30'));
  await setInput(p, 'lwm', '45');
  await p.evaluate(() => { document.querySelector('input[name="lwm"]').closest('form').requestSubmit(); }); await sleep(400);
  t = await bodyText(p);
  check('weight confirm strip shows course and minutes', t.includes('Confirm learning weight') && t.includes('Algebra II') && t.includes('45'));
  await clickText(p, 'ion-button', 'Confirm and save'); await sleep(900);
  const w = find('PUT', /^\/courses\/c1\/learning-weight$/).at(-1);
  check('weight PUT {nonVideoCourseSeconds: 2700}', !!w && !!w.body && JSON.parse(w.body).nonVideoCourseSeconds === 2700);
  await p.close();
}

// ================= TenantAdmin: webhooks =================
{
  const p = await page(admin, 'en');
  await p.goto('http://127.0.0.1:4313/app/ops', { waitUntil: 'networkidle0' }); await sleep(1200);
  await clickText(p, 'button', 'Webhooks'); await sleep(900);
  let t = await bodyText(p);
  check('webhook listed with types and failure count', t.includes('https://hooks.example.com/in') && t.includes('order.paid') && t.includes('Failures'));
  await setInput(p, 'hu', 'https://hooks.example.com/new');
  await p.evaluate(() => { document.querySelector('input[name="hu"]').closest('form').requestSubmit(); }); await sleep(900);
  const cr = find('POST', /^\/webhooks$/).at(-1);
  check('webhook create POST {url, eventTypes, isActive}', !!cr && !!cr.body && (() => { const b = JSON.parse(cr.body); return b.url === 'https://hooks.example.com/new' && b.eventTypes.includes('order.paid') && b.isActive === true; })());
  t = await bodyText(p);
  check('one-time secret shown with store-now warning', t.includes('whsec_fixturesecret') && t.includes('not shown again'));
  await p.screenshot({ path: path.join(out, 'ops-webhook-secret-en.png') }); console.log('shot ops-webhook-secret-en');
  // deliveries + retry
  await clickText(p, 'ion-button', 'Deliveries'); await sleep(900);
  t = await bodyText(p);
  check('delivery shown with status and last error', t.includes('Failed') && t.includes('Internal Server Error'));
  await clickText(p, 'ion-button', 'Retry'); await sleep(900);
  check('retry POST sent and deliveries refetched', find('POST', /^\/webhooks\/deliveries\/dl1\/retry$/).length === 1 && find('GET', /^\/webhooks\/wh1\/deliveries$/).length >= 2);
  // delete behind confirm strip
  await clickText(p, 'ion-button', 'Delete'); await sleep(400);
  t = await bodyText(p);
  check('delete confirm strip shows URL before sending', t.includes('Delete webhook endpoint') && t.includes('https://hooks.example.com/in') && find('DELETE', /^\/webhooks\/wh1$/).length === 0);
  await p.evaluate(() => { [...document.querySelectorAll('.confirm ion-button')].find((b) => (b.textContent || '').trim() === 'Delete').click(); }); await sleep(900);
  check('webhook DELETE sent', find('DELETE', /^\/webhooks\/wh1$/).length === 1);
  check('hooks refetched after delete', find('GET', /^\/webhooks$/).length >= 2);
  check('ops: no horizontal overflow', (await overflow(p)) === 0);
  await p.close();
}

// ================= student: order detail =================
{
  const p = await page(student, 'en');
  await p.goto('http://127.0.0.1:4313/app/orders', { waitUntil: 'networkidle0' }); await sleep(1200);
  await clickText(p, 'ion-button', 'Details'); await sleep(900);
  const t = await bodyText(p);
  check('order detail shows items, totals, invoice, payment', t.includes('Algebra II') && t.includes('500 EGP') && t.includes('450 EGP') && t.includes('INV-2026-0042') && t.includes('Confirmed'));
  check('order detail GET sent once', find('GET', /^\/orders\/o1$/).length === 1);
  await p.screenshot({ path: path.join(out, 'orders-detail-en.png') }); console.log('shot orders-detail-en');
  check('orders: no horizontal overflow', (await overflow(p)) === 0);
  await p.close();
}

// ================= anonymous: resend verification code =================
{
  const p = await page(null, 'en');
  await p.goto('http://127.0.0.1:4313/register', { waitUntil: 'networkidle0' }); await sleep(1200);
  const setIon = (name, value) => p.evaluate((n, v) => { const ion = document.querySelector(`ion-input[name="${n}"]`); ion.value = v; ion.dispatchEvent(new CustomEvent('ionInput', { bubbles: true, composed: true, detail: { value: v } })); }, name, value);
  await setIon('fullName', 'New Student'); await setIon('academy', 'demo'); await setIon('email', 'new@demo.test'); await setIon('password', 'Supersecret1'); await sleep(400);
  await p.evaluate(() => { document.querySelector('ion-input[name="email"]').closest('form').requestSubmit(); }); await sleep(1200);
  let t = await bodyText(p);
  check('register moved to code step', t.includes('Verification code'));
  const resendBtn = await p.evaluate(() => { const b = [...document.querySelectorAll('ion-button')].find((x) => (x.textContent || '').trim() === 'Resend the code'); if (b) { b.click(); return true; } return false; });
  check('resend button present on code step', resendBtn);
  await sleep(900);
  const otp = find('POST', /^\/auth\/otp\/request$/).at(-1);
  check('otp resend POST {destination, purpose: EmailVerification}', !!otp && !!otp.body && (() => { const b = JSON.parse(otp.body); return b.destination === 'new@demo.test' && b.purpose === 'EmailVerification'; })());
  await p.close();
}

// ================= old backend: added GETs 404 -> no blind replacing writes =================
{
  // prerequisites GET 404: picker and its replacing PUT stay inaccessible
  const p = await page(instructor, 'en', ['GET /courses/c1/prerequisites']);
  await p.goto('http://127.0.0.1:4313/app/build', { waitUntil: 'networkidle0' }); await sleep(1200);
  await clickText(p, 'button', 'Algebra II (Draft)'); await sleep(1200);
  let t = await bodyText(p);
  const before = find('PUT', /^\/courses\/c1\/prerequisites$/).length;
  check('old backend: prerequisite update-required note shown', t.includes('backend update') && t.includes('Saving stays disabled'));
  check('old backend: no prerequisite save control rendered', (await p.evaluate(() => [...document.querySelectorAll('ion-button')].some((b) => (b.textContent || '').trim() === 'Save prerequisites'))) === false);
  check('old backend: zero prerequisite PUTs', find('PUT', /^\/courses\/c1\/prerequisites$/).length === before);
  await p.close();
}
{
  // exam GET 404: the edit form never opens, so no settings-overwrite PUT is possible
  const p = await page(instructor, 'en', ['GET /exams/e1']);
  await p.goto('http://127.0.0.1:4313/app/assessments', { waitUntil: 'networkidle0' }); await sleep(1200);
  await setInput(p, 'course', 'c1'); await sleep(900);
  await clickText(p, 'ion-button', 'Edit'); await sleep(900);
  const before = find('PUT', /^\/exams\/e1$/).length;
  check('old backend: exam edit form stays closed when GET 404s', (await p.evaluate(() => !!document.querySelector('input[name="eet"]'))) === false);
  check('old backend: zero exam PUTs', find('PUT', /^\/exams\/e1$/).length === before);
  await p.close();
}
{
  // learning-weight GET 404: unavailable note, no minutes input, no PUT
  const p = await page(admin, 'en', ['GET /courses/c1/learning-weight']);
  await p.goto('http://127.0.0.1:4313/app/finance', { waitUntil: 'networkidle0' }); await sleep(1500);
  await clickText(p, 'button', 'Controls'); await sleep(900);
  await setInput(p, 'lwc', 'c1'); await sleep(900);
  const t = await bodyText(p);
  const before = find('PUT', /^\/courses\/c1\/learning-weight$/).length;
  check('old backend: learning-weight unavailable note shown', t.toLowerCase().includes('update') || t.toLowerCase().includes('unavailable'));
  check('old backend: no learning-weight save input rendered', (await p.evaluate(() => !!document.querySelector('input[name="lwm"]'))) === false);
  check('old backend: zero learning-weight PUTs', find('PUT', /^\/courses\/c1\/learning-weight$/).length === before);
  await p.close();
}

await browser.close(); server.close();
console.log('\n' + results.join('\n'));
console.log(results.every((r) => r.startsWith('PASS')) ? '\nALL PASS' : '\nSOME FAILED');
