// Round 1 screenshot harness. Serves dist/ and answers API calls from contract-shaped fixtures (NOT the real backend).
// Usage: npx ng build --configuration development && node tools/shots-round1.mjs <outDir>
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots-round1'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4301);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const TID = '11111111-1111-1111-1111-111111111111';
const admin = { id: 'u-admin', tenantId: TID, fullName: 'Hana Mostafa', email: 'admin@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['TenantAdmin'], lastLoginAtUtc: '2026-11-25T08:00:00Z' };
const superAdmin = { id: 'u-sa', tenantId: null, fullName: 'Platform Owner', email: 'super@platform.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles: ['SuperAdmin'], lastLoginAtUtc: '2026-11-24T21:00:00Z' };
const tenant = (id, name, slug, active) => ({ id, name, slug, subdomain: null, logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', timeZoneId: 'Africa/Cairo', currency: 'EGP', maxMobileDevices: 1, maxWebDevices: 1, isActive: active });
const fx = {
  '/tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'ar', featureFlags: [{ key: 'Ai.Tutor', isEnabled: true }] }),
  '/users': ok({ totalCount: 3, page: 1, pageSize: 20, items: [
    { id: 'u1', tenantId: TID, fullName: 'Dr. Karim Nabil', email: 'karim@demo.test', phoneNumber: '01001234567', preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Instructor'], lastLoginAtUtc: '2026-11-24T19:00:00Z' },
    { id: 'u2', tenantId: TID, fullName: 'Mona Adel', email: 'mona@demo.test', phoneNumber: null, preferredLanguage: 'ar', isActive: true, emailConfirmed: true, roles: ['Student'], lastLoginAtUtc: '2026-11-25T07:30:00Z' },
    { id: 'u3', tenantId: TID, fullName: 'Tarek Samy', email: 'tarek@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: false, emailConfirmed: true, roles: ['TeachingAssistant'], lastLoginAtUtc: null } ] }),
  '/users/u1/devices': ok([{ id: 'd1', deviceType: 'Web', platform: 'windows', deviceName: 'Chrome', firstSeenAtUtc: '2026-11-01T10:00:00Z', lastSeenAtUtc: '2026-11-24T19:00:00Z', isActive: true }]),
  '/courses/mine': ok([
    { id: 'c1', title: 'Algebra II', status: 'PendingApproval', ownerInstructorId: 'u1aaaaaa-1111-1111-1111-111111111111' },
    { id: 'c2', title: 'Physics Basics', status: 'PendingApproval', ownerInstructorId: 'u1aaaaaa-1111-1111-1111-111111111111' },
    { id: 'c3', title: 'Arabic Grammar', status: 'Published', ownerInstructorId: 'u1aaaaaa-1111-1111-1111-111111111111' },
    { id: 'c4', title: 'Statistics', status: 'Draft', ownerInstructorId: 'u1aaaaaa-1111-1111-1111-111111111111' } ]),
  '/tenants': ok({ totalCount: 2, page: 1, pageSize: 20, items: [tenant('t1', 'Nile Academy', 'demo', true), tenant('t2', 'Alex STEM School', 'alexstem', false)] }),
  '/tenant/feature-flags': ok([{ key: 'Ai.Tutor', isEnabled: true }, { key: 'Ai.VoiceTutor', isEnabled: false }, { key: 'Community.Chat', isEnabled: true }, { key: 'Registration.Parent', isEnabled: false }]),
  '/categories': ok([{ id: 'k1', parentCategoryId: null, name: 'Mathematics', slug: 'mathematics', sortOrder: 1, iconName: null }, { id: 'k2', parentCategoryId: 'k1', name: 'Algebra', slug: 'algebra', sortOrder: 1, iconName: null }, { id: 'k3', parentCategoryId: null, name: 'Science', slug: 'science', sortOrder: 2, iconName: null }]),
  '/me/devices': ok([]), '/notifications': ok([]),
};
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function page(w, h, lang, dark, sessionUser, tenantKey) {
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
  await p.evaluateOnNewDocument((l, s, tk) => {
    if (l) localStorage.setItem('CapacitorStorage.lang', l);
    if (tk !== undefined && tk !== null) localStorage.setItem('CapacitorStorage.tenantKey', tk);
    if (s) localStorage.setItem('CapacitorStorage.session', JSON.stringify(s));
  }, lang, sessionUser ? { accessToken: 'a', refreshToken: 'r', user: sessionUser } : null, tenantKey);
  return p;
}
const shot = async (p, url, name, settle = 900) => { await p.goto('http://127.0.0.1:4301' + url, { waitUntil: 'networkidle0' }); await new Promise((r) => setTimeout(r, settle)); await p.screenshot({ path: path.join(out, name + '.png') }); console.log('shot', name); };

// TenantAdmin desktop EN
let p = await page(1280, 860, 'en', false, admin, 'demo');
await shot(p, '/app/users', 'users-desktop-en');
await shot(p, '/app/approvals', 'approvals-desktop-en');
await shot(p, '/app/settings', 'settings-desktop-en');
// TenantAdmin mobile AR
p = await page(390, 844, 'ar', false, admin, 'demo');
await shot(p, '/app/users', 'users-mobile-ar');
await shot(p, '/app/approvals', 'approvals-mobile-ar');
await shot(p, '/app/settings', 'settings-mobile-ar', 1200);
// TenantAdmin dark desktop AR
p = await page(1280, 860, 'ar', true, admin, 'demo');
await shot(p, '/app/users', 'users-desktop-ar-dark');
await shot(p, '/app/approvals', 'approvals-desktop-ar-dark');
// SuperAdmin (platform: no tenant key)
p = await page(1280, 860, 'en', false, superAdmin, '');
await shot(p, '/app/dashboard', 'dashboard-superadmin-desktop-en');
await shot(p, '/app/tenants', 'tenants-desktop-en');
p = await page(390, 844, 'ar', false, superAdmin, '');
await shot(p, '/app/tenants', 'tenants-mobile-ar');
// Guest pages
p = await page(1280, 860, 'en', false, null, 'demo');
await shot(p, '/login', 'login-desktop-en');
await shot(p, '/register', 'register-desktop-en');
await shot(p, '/forgot', 'forgot-desktop-en');
p = await page(390, 844, 'ar', false, null, 'demo');
await shot(p, '/login', 'login-mobile-ar');
await shot(p, '/register', 'register-mobile-ar');
// login with academy editor open (platform hint visible)
p = await page(1280, 860, 'ar', false, null, 'demo');
await p.goto('http://127.0.0.1:4301/login', { waitUntil: 'networkidle0' });
await p.evaluate(() => { [...document.querySelectorAll('button')].find((b) => b.textContent.trim().length > 0 && b.classList.contains('change'))?.click(); });
await new Promise((r) => setTimeout(r, 500));
await p.screenshot({ path: path.join(out, 'login-academy-platform-hint-ar.png') }); console.log('shot login-academy-platform-hint-ar');
await browser.close(); server.close(); console.log('DONE');
