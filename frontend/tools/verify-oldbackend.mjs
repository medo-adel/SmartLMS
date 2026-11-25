// Old-backend compatibility: 404 (route missing) shows update-required notice + graceful fallback; 500 shows an error, no silent fallback.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-oldbackend'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4309);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const admin = { id: 'u-admin', tenantId: TID, fullName: 'Hana Mostafa', email: 'admin@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['TenantAdmin'], lastLoginAtUtc: null };
const mode = process.env.BACKEND_MODE ?? '404'; // '404' | '500'
const fx = {
  'GET /tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  'GET /learning-paths': ok([{ id: 'lp1', title: 'Math foundations', slug: 'x', description: 'Desc' }]),
  'GET /learning-paths/lp1': ok({ id: 'lp1', title: 'Math foundations', description: 'Desc', status: 'Published', issuesCertificate: true, courses: [{ id: 'c1', title: 'Algebra II', sortOrder: 1, isMandatory: true }] }),
  'GET /courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'Published', ownerInstructorId: 'u-in' }]),
  'GET /courses': ok({ items: [], total: 0 }),
  'GET /me/devices': ok([]), 'GET /notifications': ok([]),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
const ctx = await browser.createBrowserContext(); const p = await ctx.newPage(); await p.setViewport({ width: 1280, height: 900 });
await p.setRequestInterception(true);
p.on('request', (rq) => {
  if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
  const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
  const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
  if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
  if ((pp === '/learning-paths/manage' || pp === '/parent/links/pending')) {
    return rq.respond({ status: mode === '404' ? 404 : 500, headers: h, body: JSON.stringify({ success: false, message: mode === '404' ? 'not found' : 'boom', errors: [] }) });
  }
  const f = fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp];
  return rq.respond({ status: 200, headers: h, body: JSON.stringify(f ?? ok([])) });
});
await p.evaluateOnNewDocument((u) => {
  localStorage.setItem('CapacitorStorage.lang', 'en');
  localStorage.setItem('CapacitorStorage.tenantKey', 'demo');
  localStorage.setItem('CapacitorStorage.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', user: u }));
}, admin);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const bodyText = () => p.evaluate(() => document.body.innerText);

await p.goto('http://127.0.0.1:4309/app/paths', { waitUntil: 'networkidle0' }); await sleep(1200);
if (mode === '404') {
  check('404 manage: update-required notice shown', (await bodyText()).includes('backend build is older'));
  check('404 manage: published list still loads', (await bodyText()).includes('Math foundations'));
} else {
  check('500 manage: error state shown, no silent fallback claim', (await bodyText()).includes('Something went wrong'));
  check('500 manage: no update-required notice', !(await bodyText()).includes('backend build is older'));
}
await p.screenshot({ path: path.join(out, `paths-manage-${mode}.png`) }); console.log(`shot paths-manage-${mode}`);

await p.goto('http://127.0.0.1:4309/app/approvals', { waitUntil: 'networkidle0' }); await sleep(1200);
if (mode === '404') check('404 links: update-required notice shown', (await bodyText()).includes('backend build is older'));
else check('500 links: no update-required notice (error surfaced via toast/banner instead)', !(await bodyText()).includes('backend build is older'));
await p.screenshot({ path: path.join(out, `approvals-links-${mode}.png`) }); console.log(`shot approvals-links-${mode}`);

console.log('\n' + results.join('\n'));
console.log(process.exitCode ? 'VERIFY FAILED' : 'VERIFY PASSED');
await browser.close(); server.close();
