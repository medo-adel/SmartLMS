// Screenshot harness. Serves dist/ and answers API calls from contract-shaped fixtures (NOT the real backend).
// Usage: npx ng build && node tools/screenshots.mjs <outDir>   (needs puppeteer-core and a Chrome binary: CHROME=/path)
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import puppeteer from 'puppeteer-core';
const dist = path.resolve('dist/smartlms-frontend/browser'); const out = process.argv[2] ?? 'shots'; fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.json': 'application/json' };
const server = http.createServer((q, r) => { let p = path.join(dist, decodeURIComponent(q.url.split('?')[0])); if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(dist, 'index.html'); r.writeHead(200, { 'content-type': types[path.extname(p)] ?? 'application/octet-stream' }); fs.createReadStream(p).pipe(r); }).listen(4300);
const ok = (data) => ({ success: true, data, message: null, errors: [] });
const user = (roles, name) => ({ id: 'u-' + roles[0], tenantId: '11111111-1111-1111-1111-111111111111', fullName: name, email: roles[0].toLowerCase() + '@demo.test', phoneNumber: null, preferredLanguage: 'en', isActive: true, emailConfirmed: true, roles, lastLoginAtUtc: null });
const weeks = Array.from({ length: 10 }, (_, i) => ({ weekStart: `2026-${String(Math.floor(i / 5) + 9).padStart(2, "0")}-${String((i % 5) * 7 + 1).padStart(2, '0')}`, enrollments: [4, 7, 5, 11, 9, 14, 12, 18, 15, 21][i] }));
const fx = {
  '/tenants/current/branding': ok({ name: 'Nile Academy', slug: 'demo', logoUrl: null, primaryColor: '#0f766e', secondaryColor: '#f59e0b', accentColor: '#7c3aed', defaultLanguage: 'en', featureFlags: [] }),
  '/dashboards/student': ok({ enrollments: [{ courseId: 'c1', title: 'Algebra II', status: 'Active', progressPercent: 72, expiresAtUtc: null }, { courseId: 'c2', title: 'Physics Basics', status: 'Active', progressPercent: 35, expiresAtUtc: null }], upcomingDeadlines: [{ id: 'd1', courseId: 'c1', title: 'Quiz 4: Functions', dueAtUtc: '2026-12-02T10:00:00Z' }, { id: 'd2', courseId: 'c2', title: 'Lab report', dueAtUtc: '2026-12-05T18:00:00Z' }], xp: 1240, streak: 6, longestStreak: 14, skillRadar: [{ skill: 'Algebra', averagePercent: 82, attempts: 9 }, { skill: 'Geometry', averagePercent: 61, attempts: 5 }, { skill: 'Mechanics', averagePercent: 74, attempts: 7 }, { skill: 'Optics', averagePercent: 48, attempts: 3 }, { skill: 'Stats', averagePercent: 90, attempts: 6 }] }),
  '/dashboards/instructor': ok({ courses: 4, totalStudents: 238, enrollmentTrend: weeks, lessonDropOff: [{ lessonId: 'l1', courseId: 'c1', title: 'Intro to functions', started: 120, completed: 110, dropOffPercent: 8 }, { lessonId: 'l2', courseId: 'c1', title: 'Quadratics', started: 110, completed: 70, dropOffPercent: 36 }, { lessonId: 'l3', courseId: 'c1', title: 'Logarithms', started: 70, completed: 22, dropOffPercent: 69 }, { lessonId: 'l4', courseId: 'c2', title: 'Newton laws', started: 90, completed: 80, dropOffPercent: 11 }], quizDifficulty: [{ questionId: 'q1', text: 'Solve log2(x)=5', answers: 64, correctPercent: 31 }, { questionId: 'q2', text: 'Vertex of y=x^2-4x', answers: 71, correctPercent: 58 }], grossRevenue: 18450.5, revenueNote: 'Gross sales before platform fees and refunds.' }),
  '/dashboards/admin': ok({ kpis: { users: 1284, publishedCourses: 37, activeEnrollments: 902, revenueLast30Days: 42310.75, revenueAllTime: 311902, paidOrdersLast30Days: 188 }, queues: { coursesAwaitingApproval: 3, manualPaymentsToReview: 5, payoutsToApprove: 2, failedWebhookDeliveries: 0 }, revenueByDay: weeks.map((w, i) => ({ day: w.weekStart, revenue: 900 + i * 230 + (i % 3) * 400, orders: 5 + i })) }),
  '/parent/children': ok([{ studentId: 's1', fullName: 'Omar Hassan' }, { studentId: 's2', fullName: 'Salma Hassan' }]),
  '/parent/children/s1/summary': ok({ studentId: 's1', courses: [{ title: 'Algebra II', status: 'Active', progressPercent: 72 }, { title: 'Physics Basics', status: 'Active', progressPercent: 35 }], averageExamScorePercent: 46.5, weeklyStudyHours: 3.25, attendancePercent: null, riskAlerts: ['LowQuizScore'] }),
  '/me/devices': ok([{ id: 'dv1', deviceType: 'Web', platform: 'web', deviceName: 'Chrome', firstSeenAtUtc: '2026-11-01T10:00:00Z', lastSeenAtUtc: '2026-11-24T20:00:00Z', isActive: true }]),
  '/notifications': ok([{ id: 'n1', type: 'Info', title: 'Quiz graded', body: 'Your Quiz 3 result is ready.', isRead: false, createdAtUtc: '2026-11-24T18:00:00Z' }]),
};

const course = { id: 'c1', title: 'Algebra II', slug: 'algebra-ii', shortDescription: 'Functions, quadratics and logarithms with practice sets.', description: 'A step by step course for grade 11 students. Every lesson ends with a short quiz.', status: 'Published', level: 'Intermediate', language: 'en', estimatedHours: 18, pricingModel: 'OneTime', price: 450, currency: 'EGP', ownerInstructorId: 'i1',
  sections: [{ id: 's1', title: 'Functions', description: null, sortOrder: 1, lessons: [{ id: 'l1', title: 'Intro to functions', lessonType: 'Video', durationSeconds: 540, sortOrder: 1, isFreePreview: true }, { id: 'l2', title: 'Quadratics', lessonType: 'Video', durationSeconds: 780, sortOrder: 2, isFreePreview: false }] }, { id: 's2', title: 'Logarithms', description: null, sortOrder: 2, lessons: [{ id: 'l3', title: 'Log rules', lessonType: 'InteractiveSlides', durationSeconds: 300, sortOrder: 1, isFreePreview: false }] }] };
const list = (extra) => ({ ...course, sections: undefined, description: undefined, ...extra });
Object.assign(fx, {
  '/courses': ok({ totalCount: 4, page: 1, pageSize: 12, items: [list({}), list({ id: 'c2', title: 'Physics Basics', slug: 'physics', shortDescription: 'Mechanics, optics and lab skills.', level: 'Beginner', pricingModel: 'Free', price: 0 }), list({ id: 'c3', title: 'Arabic Grammar', slug: 'ar', shortDescription: 'Nahw and sarf for secondary school.', level: 'Intermediate', price: 300 }), list({ id: 'c4', title: 'Statistics', slug: 'stats', shortDescription: 'Data, charts and probability.', level: 'Advanced', price: 520 })] }),
  '/courses/c1': ok(course),
  '/courses/mine': ok([{ id: 'c1', title: 'Algebra II', status: 'Published', ownerInstructorId: 'i1' }, { id: 'c5', title: 'Calculus Draft', status: 'Draft', ownerInstructorId: 'i1' }]),
  '/me/enrollments': ok([{ id: 'e1', courseId: 'c1', title: 'Algebra II', status: 'Active', progressPercent: 72, enrolledAtUtc: '2026-10-01T00:00:00Z', expiresAtUtc: null }]),
  '/courses/c1/exams': ok([{ id: 'x1', title: 'Midterm: Functions', kind: 'Exam', durationMinutes: 30, maxAttempts: 2, passingScorePercent: 60 }]),
  '/learning-paths': ok([{ id: 'p1', title: 'Secondary Math Track', slug: 'math', description: 'From algebra to statistics.' }]),
  '/learning-paths/p1': ok({ id: 'p1', title: 'Secondary Math Track', description: 'From algebra to statistics.', courses: [{ id: 'c1', title: 'Algebra II', sortOrder: 1, isMandatory: true }, { id: 'c4', title: 'Statistics', sortOrder: 2, isMandatory: false }] }),
  '/lessons/l1': ok({ id: 'l1', courseId: 'c1', sectionId: 's1', title: 'Intro to functions', lessonType: 'Video', contentMarkdown: 'A function maps every input to exactly one output.', externalUrl: null, durationSeconds: 540, isFreePreview: true, prerequisiteLessonId: null }),
  '/lessons/l1/interactive-quizzes': ok([]),
  '/ai/courses/c1/tutor/chat': ok({ answer: 'A function gives exactly one output for each input. In the lesson, f(x) = x + 2 maps 3 to 5.', grounded: true, citations: [{ lessonId: 'l1', page: null, startSeconds: 95, endSeconds: 130, snippet: 'f(x) = x + 2 maps 3 to 5' }] }),
  '/ai/lessons/l1/mindmap': ok({ nodes: [{ id: 'r', label: 'Functions' }, { id: 'a', label: 'Domain and range' }, { id: 'b', label: 'Notation f(x)' }, { id: 'c', label: 'Vertical line test' }, { id: 'd', label: 'Examples' }], edges: [{ source: 'r', target: 'a' }, { source: 'r', target: 'b' }, { source: 'a', target: 'c' }, { source: 'b', target: 'd' }] }),
  '/wallet': ok({ balance: 275.5, currency: 'EGP' }),
  '/wallet/transactions': ok({ totalCount: 2, page: 1, pageSize: 20, items: [{ id: 't1', type: 'TopUpVoucher', amount: 300, balanceAfter: 300, description: 'Voucher top-up', createdAtUtc: '2026-11-20T10:00:00Z' }, { id: 't2', type: 'LessonUnlock', amount: -24.5, balanceAfter: 275.5, description: 'Unlocked: Quadratics', createdAtUtc: '2026-11-22T12:00:00Z' }] }),
  '/manual-payments/mine': ok([{ id: 'm1', purpose: 'WalletTopUp', method: 'VodafoneCash', amount: 200, status: 'Pending', rejectionReason: null, createdAtUtc: '2026-11-24T09:00:00Z' }]),
  '/manual-payments': ok({ totalCount: 2, page: 1, pageSize: 20, items: [{ id: 'm1', userId: 'u1', userName: 'Mona Adel', purpose: 'Order', method: 'InstaPay', amount: 450, orderId: 'o1', senderReference: 'IP-88213', senderPhoneOrAccount: '0100xxxx', status: 'Pending', createdAtUtc: '2026-11-24T18:00:00Z' }, { id: 'm2', userId: 'u2', userName: 'Omar Salah', purpose: 'WalletTopUp', method: 'VodafoneCash', amount: 200, orderId: null, senderReference: null, senderPhoneOrAccount: '0111xxxx', status: 'Pending', createdAtUtc: '2026-11-24T19:30:00Z' }] }),
  '/voucher-batches': ok({ totalCount: 1, page: 1, pageSize: 20, items: [{ id: 'vb1', name: 'Cairo centre Nov', prefix: 'CAI', quantity: 100, voucherType: 'WalletCredit', courseId: null, walletAmount: 100, expiresAtUtc: null, status: 'Ready', createdAtUtc: '2026-11-20T10:00:00Z', unused: 92, redeemed: 8, voided: 0 }] }),
  '/cart': ok({ items: [{ id: 'ci1', itemType: 'Course', itemId: 'c1', description: 'Algebra II', price: 450, currency: 'EGP', available: true }], subtotal: 450, currency: 'EGP' }),
  '/attempts/a1': ok({ attemptId: 'a1', expiresAtUtc: null, remainingSeconds: 1500, questions: [
    { attemptQuestionId: 'q1', type: 'McqSingle', text: 'Which of these is a function?', points: 2, savedAnswer: { optionId: 'o2' }, options: [{ id: 'o1', text: 'x = y^2' }, { id: 'o2', text: 'y = 2x + 1' }, { id: 'o3', text: 'x^2 + y^2 = 1' }], matchTargets: null },
    { attemptQuestionId: 'q2', type: 'McqMulti', text: 'Select the even numbers.', points: 2, savedAnswer: null, options: [{ id: 'p1', text: '2' }, { id: 'p2', text: '7' }, { id: 'p3', text: '10' }], matchTargets: null },
    { attemptQuestionId: 'q3', type: 'ShortAnswer', text: 'State the vertex of y = x^2 - 4x.', points: 3, savedAnswer: null, options: null, matchTargets: null }] }),
});
Object.assign(fx, {
  '/lessons/l1/chapters': ok([{ id: 'ch1', title: 'What is a function', startSeconds: 0, source: 'Manual' }, { id: 'ch2', title: 'Notation f(x)', startSeconds: 95, source: 'Manual' }, { id: 'ch3', title: 'Vertical line test', startSeconds: 310, source: 'Manual' }]),
  '/lessons/l1/resources': ok([{ id: 'r1', title: 'Worksheet 1', isDownloadable: true, contentType: 'application/pdf', sizeBytes: 182000, fileName: 'worksheet-1.pdf' }]),
  '/lessons/l1/notes': ok([{ id: 'n1', timestampSeconds: 95, body: 'Remember: one output per input.', createdAtUtc: '2026-11-24T18:00:00Z' }]),
  '/lessons/l1/bookmarks': ok([{ id: 'b1', timestampSeconds: 310, label: 'Review this part' }]),
  '/lessons/l3/slides': ok([{ id: 'sl1', sortOrder: 1, content: { title: 'Roots of a quadratic', body: 'Use the discriminant to count real roots.' } }, { id: 'sl2', sortOrder: 2, content: { title: 'Example', body: 'x^2 - 5x + 6 = 0 gives x = 2 or x = 3.' } }]),
  '/orders': ok({ totalCount: 2, page: 1, pageSize: 20, items: [{ id: 'o1', status: 'Paid', subtotal: 450, discountTotal: 45, total: 405, currency: 'EGP', paymentMethod: 'Wallet', createdAtUtc: '2026-11-20T10:00:00Z', paidAtUtc: '2026-11-20T10:00:05Z' }, { id: 'o2', status: 'AwaitingManualReview', subtotal: 300, discountTotal: 0, total: 300, currency: 'EGP', paymentMethod: 'Manual', createdAtUtc: '2026-11-23T10:00:00Z', paidAtUtc: null }] }),
  '/affiliate/me': ok({ referralCode: 'MONA10', referralLink: '/register?ref=MONA10', commissionPercent: 10, referrals: [{ referredUserId: 'u9', name: 'Sara Ali', createdAtUtc: '2026-11-18T10:00:00Z' }], commissions: [{ id: 'cm1', orderId: 'o1', amount: 40.5, status: 'Credited', createdAtUtc: '2026-11-20T10:00:05Z' }], totalCredited: 40.5 }),
  '/bundles': ok([{ id: 'bn1', name: 'Science pack', description: 'Physics and Algebra together', price: 600, currency: 'EGP', courses: [{ id: 'c1', title: 'Algebra II' }, { id: 'c2', title: 'Physics Basics' }] }]),
  '/subscription-plans': ok([{ id: 'pl1', name: 'All access monthly', description: 'Every course', price: 199, currency: 'EGP', billingPeriod: 'Monthly', scope: 'AllCourses' }]),
  '/coupons': ok([{ id: 'cp1', code: 'SAVE10', discountType: 'Percentage', value: 10, maxRedemptions: 100, maxPerUser: 1, minOrderAmount: null, expiresAtUtc: '2026-12-31T00:00:00Z', courseId: null, isActive: true, redemptions: 12 }, { id: 'cp2', code: 'EID50', discountType: 'Fixed', value: 50, maxRedemptions: null, maxPerUser: 1, minOrderAmount: 200, expiresAtUtc: null, courseId: null, isActive: false, redemptions: 3 }]),
  '/courses/c1/question-banks': ok([{ id: 'qb1', name: 'Functions bank', topic: 'Functions', questionCount: 2 }]),
  '/question-banks/qb1/questions': ok([{ id: 'qq1', questionType: 'McqSingle', text: 'Which is a function?', points: 2, difficulty: 'Easy', topic: null, explanation: null, options: [{ id: 'a', text: 'y = 2x + 1', isCorrect: true, matchKey: null }, { id: 'b', text: 'x = y^2', isCorrect: false, matchKey: null }] }]),
  '/lessons/l1/interactive-quizzes': ok([{ id: 'ip1', timestampSeconds: 120, isRequired: true, allowSkipAfterAttempts: 2, question: { id: 'qq1', text: 'Which is a function?', questionType: 'McqSingle' } }]),
});
const dyn = { 'POST /orders/checkout': ok({ orderId: 'o1', status: 'AwaitingManualReview', total: 450, currency: 'EGP', next: 'Transfer the amount, then POST /manual-payments with this orderId and the receipt image.' }), 'POST /wallet/topup/gateway': ok({ paymentId: 'p1', checkoutUrl: 'https://pay.example.test/checkout/abc', reference: 'ref1' }), 'POST /exams/x1/attempts': ok({ attemptId: 'a1', resumed: false }), 'POST /ai/lessons/l1/summarize': ok({ takeaways: ['A function has one output per input.'], keyTerms: ['domain', 'range'], chapters: [{ title: 'Definition', startSeconds: 20 }] }) };
const video = fs.readFileSync(new URL('./mock-lesson.webm', import.meta.url));
const accounts = { 'student@demo.test': ['Student', 'Mona Adel'], 'instructor@demo.test': ['Instructor', 'Dr. Karim Nabil'], 'admin@demo.test': ['TenantAdmin', 'Hana Mostafa'], 'parent@demo.test': ['Parent', 'Hassan Ibrahim'] };
const browser = await puppeteer.launch({ executablePath: process.env.CHROME ?? '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
async function session(w, h, lang) {
  const ctx = await browser.createBrowserContext(); const page = await ctx.newPage(); await page.setViewport({ width: w, height: h, deviceScaleFactor: 1 });
  await page.setRequestInterception(true);
  page.on('request', (rq) => {
    const u = new URL(rq.url()); if (!rq.url().startsWith('https://localhost:7200/api/v1')) return rq.continue();
    const p = u.pathname.replace('/api/v1', ''); const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'content-type': 'application/json' };
    if (rq.method() === 'OPTIONS') return rq.respond({ status: 204, headers: h });
    if (p === '/auth/login') { const b = JSON.parse(rq.postData()); const a = accounts[b.email]; return a ? rq.respond({ status: 200, headers: h, body: JSON.stringify(ok({ accessToken: 'a', accessTokenExpiresAtUtc: '2030-01-01T00:00:00Z', refreshToken: 'r', refreshTokenExpiresAtUtc: '2030-01-01T00:00:00Z', user: user([a[0]], a[1]) })) }) : rq.respond({ status: 401, headers: h, body: JSON.stringify({ success: false, message: 'Invalid credentials' }) }); }
    if (p === '/lessons/l1/stream') return rq.respond({ status: 200, headers: { ...h, 'content-type': 'video/webm' }, body: video });
    if (dyn[rq.method() + ' ' + p]) return rq.respond({ status: 200, headers: h, body: JSON.stringify(dyn[rq.method() + ' ' + p]) });
    if (fx[p]) return rq.respond({ status: 200, headers: h, body: JSON.stringify(fx[p]) });
    rq.respond({ status: 404, headers: h, body: JSON.stringify({ success: false, message: 'not in mock' }) });
  });
  await page.evaluateOnNewDocument((l) => { if (l) localStorage.setItem('CapacitorStorage.lang', l); }, lang);
  return page;
}
async function login(page, email, file) {
  await page.goto('http://localhost:4300/login', { waitUntil: 'networkidle0' });
  await page.type('ion-input[name=email] input', email); await page.type('ion-input[name=password] input', 'Passw0rd!');
  await page.click('ion-button[type=submit]'); await page.waitForSelector('app-shell', { timeout: 10000 }); await new Promise((r) => setTimeout(r, 1500));
  await page.screenshot({ path: `${out}/${file}.png` });
}
const D = [1280, 800], M = [390, 844];
let p = await session(...D); await p.goto('http://localhost:4300/login', { waitUntil: 'networkidle0' }); await new Promise((r) => setTimeout(r, 800)); await p.screenshot({ path: `${out}/01-login-en-desktop.png` }); await p.close();
p = await session(...M, 'ar'); await p.goto('http://localhost:4300/login', { waitUntil: 'networkidle0' }); await new Promise((r) => setTimeout(r, 800)); await p.screenshot({ path: `${out}/02-login-ar-rtl-mobile.png` }); await p.close();
p = await session(...D); await login(p, 'student@demo.test', '03-student-desktop-splitpane'); await p.close();
p = await session(...M); await login(p, 'student@demo.test', '04-student-mobile-tabs'); await p.close();
p = await session(...D); await login(p, 'instructor@demo.test', '05-instructor-desktop'); await p.close();
p = await session(...D); await login(p, 'admin@demo.test', '06-admin-desktop'); await p.close();
p = await session(...D); await login(p, 'parent@demo.test', '07-parent-desktop'); await p.close();
p = await session(...M, 'ar'); await login(p, 'admin@demo.test', '08-admin-ar-rtl-mobile'); await p.close();
p = await session(...D); await login(p, 'student@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/notifications', { waitUntil: 'networkidle0' }); await new Promise((r) => setTimeout(r, 1000)); await p.screenshot({ path: `${out}/09-notifications-desktop.png` }); await p.close();
p = await session(...M); await login(p, 'student@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/profile', { waitUntil: 'networkidle0' }); await new Promise((r) => setTimeout(r, 1000)); await p.screenshot({ path: `${out}/10-profile-mobile.png` }); await p.close();

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
async function visit(page, url, file, ms = 1500) { await page.goto('http://localhost:4300' + url, { waitUntil: 'networkidle0' }); await wait(ms); await page.screenshot({ path: `${out}/${file}.png` }); }
p = await session(...D); await login(p, 'student@demo.test', 'tmp');
await visit(p, '/app/courses', '11-catalog-desktop'); await visit(p, '/app/courses/c1', '12-course-detail-desktop');
await p.goto('http://localhost:4300/app/learn/c1/l1', { waitUntil: 'networkidle0' }); await wait(1500);
await p.click('ion-button ::-p-text(Ask the AI tutor)'); await wait(500); await p.type('app-ai-drawer input', 'What is a function?'); await p.keyboard.press('Enter'); await wait(1200);
await p.screenshot({ path: `${out}/13-player-ai-tutor-desktop.png` });
await p.click('app-ai-drawer nav button:nth-child(4)'); await wait(1500); await p.screenshot({ path: `${out}/14-player-mindmap-desktop.png` });
await visit(p, '/app/wallet', '15-wallet-desktop'); await visit(p, '/app/ai', '16-ai-studio-desktop');
await p.close();
p = await session(...D); await login(p, 'student@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/exam/x1', { waitUntil: 'networkidle0' }); await wait(1500); await p.screenshot({ path: `${out}/17-exam-desktop.png` }); await p.close();
p = await session(...M, 'ar'); await login(p, 'student@demo.test', 'tmp'); await visit(p, '/app/courses', '18-catalog-ar-rtl-mobile'); await visit(p, '/app/cart', '19-cart-ar-rtl-mobile'); await p.close();
p = await session(...D); await login(p, 'instructor@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/build', { waitUntil: 'networkidle0' }); await wait(800); await p.click('.tabs2 button'); await wait(1200); await p.screenshot({ path: `${out}/20-builder-desktop.png` }); await p.close();
p = await session(...D); await login(p, 'student@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/cart', { waitUntil: 'networkidle0' }); await wait(800);
await p.click('.tabs2 button:nth-child(4)'); await wait(300); await p.click('ion-button ::-p-text(Checkout)'); await wait(1200); await p.screenshot({ path: `${out}/21-cart-manual-receipt-desktop.png` });
await p.goto('http://localhost:4300/app/wallet', { waitUntil: 'networkidle0' }); await wait(800); await p.type('input[name=tu]', '200'); await p.click('ion-button ::-p-text(Continue to payment)'); await wait(1000); await p.screenshot({ path: `${out}/22-wallet-gateway-desktop.png`, fullPage: true }); await p.close();
p = await session(...D); await login(p, 'instructor@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/build', { waitUntil: 'networkidle0' }); await wait(800); await p.click('.tabs2 button'); await wait(1200);
await p.click('ion-button ::-p-text(Chapters)'); await wait(500); await p.screenshot({ path: `${out}/23-builder-edit-chapters-desktop.png`, fullPage: true }); await p.close();
p = await session(...D); await login(p, 'admin@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/payments', { waitUntil: 'networkidle0' }); await wait(1000); await p.screenshot({ path: `${out}/24-admin-payments-desktop.png`, fullPage: true }); await p.close();
p = await session(...M, 'ar'); await login(p, 'admin@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/payments', { waitUntil: 'networkidle0' }); await wait(1000); await p.screenshot({ path: `${out}/25-admin-payments-ar-rtl-mobile.png` }); await p.close();
p = await session(1280, 1700); await login(p, 'student@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/learn/c1/l1', { waitUntil: 'networkidle0' }); await wait(2000); await p.screenshot({ path: `${out}/26-player-chapters-notes-desktop.png`, fullPage: true }); await p.close();
p = await session(1280, 1700); await login(p, 'student@demo.test', 'tmp'); await visit(p, '/app/orders', '27-orders-affiliate-desktop', 1500); await visit(p, '/app/courses', '28-catalog-bundles-plans-desktop', 1800); await p.close();
p = await session(1280, 1700); await login(p, 'instructor@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/assessments', { waitUntil: 'networkidle0' }); await wait(800); await p.select('select[name=course]', 'c1'); await wait(800); await p.select('select[name=bank]', 'qb1'); await wait(800); await p.select('select[name=pl]', 'l1').catch(() => {}); await wait(800); await p.screenshot({ path: `${out}/29-assessments-authoring-desktop.png`, fullPage: true }); await p.close();
p = await session(1280, 1700); await login(p, 'instructor@demo.test', 'tmp'); await p.goto('http://localhost:4300/app/build', { waitUntil: 'networkidle0' }); await wait(800); await p.click('.tabs2 button'); await wait(1200); await p.click('ion-button ::-p-text(Slides)'); await wait(800); await p.screenshot({ path: `${out}/30-builder-pricing-desktop.png`, fullPage: true }); await p.close();
p = await session(1280, 1700); await login(p, 'admin@demo.test', 'tmp'); await visit(p, '/app/monetization', '31-monetization-admin-desktop', 1500); await p.close();
p = await session(390, 1500, 'ar'); await login(p, 'admin@demo.test', 'tmp'); await visit(p, '/app/monetization', '32-monetization-ar-rtl-mobile', 1500); await p.close(); p = await session(390, 1500, 'ar'); await login(p, 'student@demo.test', 'tmp'); await visit(p, '/app/orders', '33-orders-ar-rtl-mobile', 1500); await p.close();
await browser.close(); server.close(); console.log('done');
