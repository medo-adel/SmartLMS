// Builder resource-attachment verification. Real built UI in headless Chrome, real file selection on the
// picker, fixture HTTP that stores what it receives (NOT the real backend).
// Asserts the reported defect is fixed: picking an attachment uploaded fine on the server but the builder
// showed nothing, so the file looked "added then removed".
//  1. The picker really uploads: the fixture server receives the multipart POST with the chosen file name.
//  2. After upload the attachment stays visible under the lesson (name + size + download button).
//  3. Reload + reopen the course: the attachment is still listed (persistence, from the server's list).
//  4. The download button calls the download endpoint and gets the stored bytes back.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-builder-resources'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4312);
const ok = (data) => ({ success: true, data, message: null, errors: [] });

const lesson = { id: 'les-1', title: 'Lesson One', lessonType: 'Video', durationSeconds: null, sortOrder: 0, isFreePreview: false };
const course = { id: 'c-1', title: 'Test Course', slug: 'test-course', shortDescription: null, level: 'Beginner', language: null, estimatedHours: null, pricingModel: 'Free', price: 0, currency: 'EGP', description: null, status: 'Draft', sections: [{ id: 'sec-1', title: 'Section One', description: null, sortOrder: 0, lessons: [lesson] }] };
const stored = [];   // resources the fixture "server" holds
const uploads = [];  // captured multipart POSTs
const downloads = []; // captured download GETs

const fx = {
  'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
  'GET /learning-paths/manage': ok([]), 'GET /learning-paths': ok([]),
  'GET /courses': ok({ items: [], totalCount: 0 }),
  'GET /courses/mine': ok([{ id: 'c-1', title: 'Test Course', status: 'Draft', ownerInstructorId: 'u-inst' }]),
  'GET /courses/c-1': ok(course),
  'GET /courses/c-1/staff': ok([]), 'GET /courses/c-1/prerequisites': ok([]),
  'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const p = await (await browser.createBrowserContext()).newPage();
await p.setViewport({ width: 1280, height: 800 });
const consoleProblems = [];
p.on('pageerror', (e) => consoleProblems.push('pageerror: ' + e.message));
p.setRequestInterception(true);
p.on('request', (rq) => { void (async () => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  if (rq.method() === 'POST' && pp === '/lessons/les-1/resources') {
    const body = (typeof rq.fetchPostData === 'function' ? await rq.fetchPostData().catch(() => '') : '') || rq.postData() || '';
    const m = body.match(/filename="([^"]+)"/);
    uploads.push({ filename: m?.[1] ?? null, bytes: Buffer.byteLength(body) });
    const r = { id: 'r-1', title: m?.[1] ?? 'file', isDownloadable: true, contentType: 'application/pdf', sizeBytes: 93871, fileName: m?.[1] ?? 'file' };
    stored.push(r);
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(r)) });
  }
  if (rq.method() === 'GET' && pp === '/lessons/les-1/resources') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok(stored)) });
  if (rq.method() === 'GET' && pp === '/lessons/les-1/resources/r-1/download') { downloads.push(pp); return rq.respond({ status: 200, headers: { ...h, 'content-type': 'application/pdf' }, body: Buffer.from('%PDF-1.4 fixture bytes') }); }
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok({ id: 'x' })) });
})().catch((e) => { consoleProblems.push('handler: ' + e.message); try { rq.abort(); } catch { /* already handled */ } });
});
await p.evaluateOnNewDocument(() => {
  if (localStorage.getItem('CapacitorStorage.seeded')) return;
  localStorage.setItem('CapacitorStorage.seeded', '1');
  localStorage.setItem('CapacitorStorage.lang', 'ar');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: { id: 'u-inst', tenantId: 't-1', fullName: 'Instructor One', email: 'inst@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student', 'Instructor'], lastLoginAtUtc: null } }));
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };

// a real small PDF-like file for the picker
const pick = path.resolve('tools/test-attachment.pdf');
if (!fs.existsSync(pick)) fs.writeFileSync(pick, '%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');

await p.goto('http://127.0.0.1:4312/app/build', { waitUntil: 'networkidle0' }); await sleep(1200);
await p.evaluate(() => { const el = [...document.querySelectorAll('.tabs2 button')].find((x) => (x.textContent || '').includes('Test Course')); el?.click(); });
await sleep(1500);
check('course opened, lesson row visible', await p.$('input[type=file][accept*=".zip"]') !== null);

// real file selection on the resource picker
const input = await p.$('input[type=file][accept*=".zip"]');
await input.uploadFile(pick);
await p.waitForFunction(() => document.querySelector('.reslist li span')?.textContent?.includes('test-attachment.pdf'), { timeout: 8000 }).catch(() => {});
const afterUpload = await p.evaluate(() => ({ item: document.querySelector('.reslist li span')?.textContent?.trim() ?? '', pickerCleared: document.querySelector('input[type=file][accept*=".zip"]')?.files?.length === 0 }));
check('server received the chosen file', uploads.length === 1 && uploads[0].filename === 'test-attachment.pdf');
check('attachment stays listed after upload (name + size)', /test-attachment\.pdf/.test(afterUpload.item) && /KB|MB/.test(afterUpload.item));
await p.evaluate(() => document.querySelector('.reslist')?.scrollIntoView({ block: 'center' })); await sleep(300);
await p.screenshot({ path: path.join(out, 'builder-resource-listed-ar.png') });

// reload + reopen: list comes from the server, so the attachment survives
await p.reload({ waitUntil: 'networkidle0' }); await sleep(1200);
await p.evaluate(() => { const el = [...document.querySelectorAll('.tabs2 button')].find((x) => (x.textContent || '').includes('Test Course')); el?.click(); });
await p.waitForFunction(() => document.querySelector('.reslist li span')?.textContent?.includes('test-attachment.pdf'), { timeout: 8000 }).catch(() => {});
const afterReload = await p.evaluate(() => document.querySelector('.reslist li span')?.textContent?.trim() ?? '');
check('attachment still listed after reload + reopen (persistence)', /test-attachment\.pdf/.test(afterReload));

// download button hits the download endpoint
await p.evaluate(() => { const li = document.querySelector('.reslist li'); li?.querySelector('ion-button')?.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
await sleep(800);
check('download button fetched the stored file', downloads.length === 1);
check('no page errors', consoleProblems.length === 0);
if (consoleProblems.length) console.log(consoleProblems.join('\n'));

console.log(results.join('\n'));
await browser.close(); server.close();
