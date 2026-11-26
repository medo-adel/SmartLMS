// Google social login: the login page probes GET /auth/external-providers and only then mounts the GSI
// button. Real built UI in headless Chrome, fixture HTTP (NOT the real backend), window.google stubbed
// (the real GSI script needs network + a real client id). Asserts: the button renders only when the
// backend lists google, clicking it POSTs the credential to /auth/social/google with the device payload,
// a successful response stores the session and lands on the dashboard, and an API error surfaces inline.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-social'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4313);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const results = []; const check = (name, cond) => { results.push((cond ? 'PASS' : 'FAIL') + ' ' + name); if (!cond) process.exitCode = 1; };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Minimal GSI stand-in: renders a button that yields the credential the real flow would return.
const GSI_STUB = `window.__gsiInit = []; window.google = { accounts: { id: {
  initialize(cfg) { window.__gsiInit.push(cfg); },
  renderButton(el, opts) { const b = document.createElement('button'); b.type = 'button'; b.id = 'gsi-stub-btn';
    b.style.cssText = 'inline-size:100%;block-size:2.75rem;border-radius:8px;border:1px solid #dadce0;background:#fff;font:500 1rem Roboto,sans-serif;color:#3c4043;cursor:pointer';
    b.textContent = 'Continue with Google';
    b.addEventListener('click', () => window.__gsiInit.at(-1).callback({ credential: 'valid-google-token' })); el.replaceChildren(b); },
} } };`;

const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });

async function boot(providers, socialHandler) {
  const ctx = await browser.createBrowserContext(); const p = await ctx.newPage();
  await p.setViewport({ width: 390, height: 844 });
  const problems = []; p.on('pageerror', (e) => problems.push('pageerror: ' + e.message));
  const calls = [];
  await p.setRequestInterception(true);
  p.on('request', (rq) => { void (async () => {
    if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const u = new URL(rq.url()); const pp = u.pathname.replace('/api/v1', '');
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (pp === '/auth/external-providers') return rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ providers })) });
    if (pp === '/auth/social/google' && rq.method() === 'POST') { calls.push(rq.postData() ?? ''); return socialHandler(rq, h); }
    const fx = { 'GET /tenants/current/branding': ok({ name: 'Test Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [] }), 'GET /auth/mail-delivery': ok({ mode: 'local-outbox' }), 'GET /me/devices': ok([]), 'GET /notifications': ok([]),
      'GET /dashboards/student': ok({ enrollments: [{ courseId: 'c-1', title: 'أساسيات الجبر', status: 'Active', progressPercent: 40, expiresAtUtc: null }], upcomingDeadlines: [], xp: 120, streak: 3, longestStreak: 9, skillRadar: [{ skill: 'جبر', averagePercent: 72, attempts: 4 }] }) };
    return rq.respond({ status: 200, headers: h, body: JSON.stringify(fx[rq.method() + ' ' + pp] ?? fx['GET ' + pp] ?? ok({})) });
  })().catch((e) => { problems.push('fixture: ' + e.message); rq.abort(); }); });
  await p.evaluateOnNewDocument((stub, tenant) => {
    eval(stub);
    localStorage.setItem('CapacitorStorage.lang', 'ar');
    localStorage.setItem('CapacitorStorage.tenantKey', tenant);
  }, GSI_STUB, 'demo');
  return { p, ctx, problems, calls };
}

// 1) google configured: divider + button appear, click signs in and navigates
{
  const { p, ctx, problems, calls } = await boot([{ provider: 'google', clientId: 'test-google-client-id' }], (rq, h) =>
    rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ accessToken: 'acc-g', accessTokenExpiresAtUtc: '2027-01-01T00:00:00Z', refreshToken: 'ref-g', refreshTokenExpiresAtUtc: '2027-01-01T00:00:00Z', user: { id: 'u-g', tenantId: 't-1', fullName: 'مستخدم جوجل', email: 'social@mail.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: null } })) }));
  await p.goto('http://127.0.0.1:4313/login', { waitUntil: 'networkidle0' }); await sleep(1800);
  const btn = await p.evaluate(() => { const c = document.querySelector('[data-testid="google-signin"]'); const b = document.querySelector('#gsi-stub-btn'); return c && b ? { text: b.textContent, overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth, init: window.__gsiInit[0]?.client_id } : null; });
  check('login: Google button mounts with the probed client id, no 390px overflow', !!btn && btn.init === 'test-google-client-id' && btn.text.includes('Google') && btn.overflow <= 1);
  check('login: divider shows Arabic or', await p.evaluate(() => document.querySelector('.or')?.textContent.trim() === 'أو'));
  await p.screenshot({ path: path.join(out, 'login-google-ar.png') });
  await p.evaluate(() => document.querySelector('#gsi-stub-btn').click()); await sleep(1500);
  check('login: click POSTs the credential + device to /auth/social/google', calls.length === 1 && calls[0].includes('"idToken":"valid-google-token"') && calls[0].includes('"device"'));
  check('login: success stores session and lands on the dashboard', await p.evaluate(() => location.pathname.startsWith('/app') && !!localStorage.getItem('CapacitorStorage.session')?.includes('u-g')));
  await sleep(1500);
  check('login: dashboard settled (real content, no skeletons)', await p.evaluate(() => !document.querySelector('app-dash-skeleton') && (document.body.textContent ?? '').includes('أساسيات الجبر')));
  check('login: no page errors', problems.length === 0);
  await p.screenshot({ path: path.join(out, 'login-google-after-ar.png') });
  await ctx.close();
}

// 2) provider sign-in rejected by the API: error surfaces inline, no navigation
{
  const { p, ctx } = await boot([{ provider: 'google', clientId: 'test-google-client-id' }], (rq, h) =>
    rq.respond({ status: 403, headers: h, body: JSON.stringify({ success: false, data: null, message: 'Privileged accounts sign in with email and password.', code: 'SOCIAL_LINK_NOT_ALLOWED', errors: [] }) }));
  await p.goto('http://127.0.0.1:4313/login', { waitUntil: 'networkidle0' }); await sleep(1800);
  await p.evaluate(() => document.querySelector('#gsi-stub-btn').click()); await sleep(1200);
  check('login: API rejection surfaces the server message and stays on /login', await p.evaluate(() => location.pathname === '/login' && (document.querySelector('.error')?.textContent ?? '').includes('email and password')));
  await ctx.close();
}

// 3) no providers configured: no Google UI at all
{
  const { p, ctx } = await boot([], (rq, h) => rq.respond({ status: 500, headers: h, body: '{}' }));
  await p.goto('http://127.0.0.1:4313/login', { waitUntil: 'networkidle0' }); await sleep(1800);
  check('login: empty provider list hides the Google button and divider', await p.evaluate(() => !document.querySelector('[data-testid="google-signin"]') && !document.querySelector('.or')));
  await ctx.close();
}

console.log(results.join('\n')); console.log(problemsSummary());
function problemsSummary() { return process.exitCode ? 'FAILURES PRESENT' : 'all green'; }
await browser.close(); server.close();
