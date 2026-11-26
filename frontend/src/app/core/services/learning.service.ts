import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SILENT_ERRORS } from '../interceptors/error.interceptor';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import {
  AiMindMap, AiQuizDraft, AiSearchHit, AiStudyPlan, AiSummary, AiTutorResult, AiVoice, AnswerValue, AttemptResult, AttemptView, CartView, CheckoutOutcome, OrderRow, CouponPreview, CourseDetail, CourseListItem,
  CourseProgress, Enrollment, ExamListItem, LessonNote, LessonBookmark, LessonChapter, LessonSlide, LessonResource, Bundle, Plan, AffiliateMe, QuestionBank, QuestionDto, QuestionInput, ExamInput, QuizPointDto, ReviewItem, VoucherBatch, InteractiveQuiz, Lesson, ManualPayment, MyCourse, Paged, PathDetail, PathListItem, PathRequest, WalletBalance, WalletTx,
  CouponDto, CouponInput, ExamDetail, ExamAttemptRow, GradingView, CourseStaffRow, OrderDetail, WishlistItem } from '../models/learning';

/** Phase 7 calls. Everything goes to the .NET API; the AI service is only reached through /ai endpoints. */
@Injectable({ providedIn: 'root' })
export class LearningService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  private async unwrap<T>(req: Promise<ApiResponse<T>>): Promise<T> {
    const res = await req;
    if (!res.success) throw new Error(res.message ?? 'request failed');
    return res.data as T;
  }
  private get<T>(path: string, params?: HttpParams) { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<T>>(`${this.base}${path}`, { params }))); }
  private post<T>(path: string, body: unknown = {}) { return this.unwrap(firstValueFrom(this.http.post<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private put<T>(path: string, body: unknown, silent = false) { return this.unwrap(firstValueFrom(this.http.put<ApiResponse<T>>(`${this.base}${path}`, body, silent ? { context: new HttpContext().set(SILENT_ERRORS, true) } : {}))); }
  private id(v: string) { return encodeURIComponent(v); }

  // catalog and paths
  courses(search: string, page = 1) {
    let p = new HttpParams().set('page', page).set('pageSize', 12);
    if (search.trim()) p = p.set('q', search.trim());
    return this.get<Paged<CourseListItem>>('/courses', p);
  }
  course(id: string) { return this.get<CourseDetail>(`/courses/${this.id(id)}`); }
  myEnrollments() { return this.get<Enrollment[]>('/me/enrollments'); }
  enroll(courseId: string) { return this.post<unknown>(`/courses/${this.id(courseId)}/enroll`); }
  paths() { return this.get<PathListItem[]>('/learning-paths'); }
  path(id: string) { return this.get<PathDetail>(`/learning-paths/${this.id(id)}`); }
  enrollPath(id: string) { return this.post<unknown>(`/learning-paths/${this.id(id)}/enroll`); }
  /** Staff list incl. drafts (added backend route; falls back to the public published-only list on older backends). */
  async managePaths(): Promise<(PathListItem & { status?: string })[]> { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<(PathListItem & { status?: string })[]>>(`${this.base}/learning-paths/manage`, { context: new HttpContext().set(SILENT_ERRORS, true) }))); }
  createPath(r: PathRequest) { return this.post<{ id: string }>('/learning-paths', r); }
  updatePath(id: string, r: PathRequest) { return this.put(`/learning-paths/${this.id(id)}`, r); }
  deletePath(id: string) { return this.del(`/learning-paths/${this.id(id)}`); }

  // player
  lesson(id: string) { return this.get<Lesson>(`/lessons/${this.id(id)}`); }
  courseProgress(courseId: string) { return this.get<CourseProgress>(`/courses/${this.id(courseId)}/progress`); }
  saveProgress(lessonId: string, lastPositionSeconds: number, watchedSecondsDelta: number, completed: boolean, playbackSpeed: number, recordedAtUtc: string | null = null) {
    return this.put<unknown>(`/lessons/${this.id(lessonId)}/progress`, { lastPositionSeconds, watchedSecondsDelta, completed, playbackSpeed, ...(recordedAtUtc ? { recordedAtUtc } : {}) }, true);
  }
  /** Protected media: fetched with the bearer token (interceptor) and played from a blob URL, never a public link. */
  streamBlob(lessonId: string) { return firstValueFrom(this.http.get(`${this.base}/lessons/${this.id(lessonId)}/stream`, { responseType: 'blob' })); }
  interactiveQuizzes(lessonId: string) { return this.get<InteractiveQuiz[]>(`/lessons/${this.id(lessonId)}/interactive-quizzes`); }
  answerInteractiveQuiz(id: string, answer: AnswerValue) { return this.post<unknown>(`/interactive-quizzes/${this.id(id)}/answer`, { answer }); }

  // exams
  exams(courseId: string) { return this.get<ExamListItem[]>(`/courses/${this.id(courseId)}/exams`); }
  startAttempt(examId: string) { return this.post<{ attemptId: string }>(`/exams/${this.id(examId)}/attempts`); }
  attempt(id: string) { return this.get<AttemptView>(`/attempts/${this.id(id)}`); }
  saveAnswers(id: string, answers: { attemptQuestionId: string; answer: AnswerValue }[]) { return this.put<unknown>(`/attempts/${this.id(id)}/answers`, { answers }); }
  attemptEvent(id: string, eventType: string) { return this.post<unknown>(`/attempts/${this.id(id)}/events`, { eventType }); }
  submitAttempt(id: string) { return this.post<AttemptResult>(`/attempts/${this.id(id)}/submit`); }
  attemptResult(id: string) { return this.get<AttemptResult>(`/attempts/${this.id(id)}/result`); }

  // cart, wallet, payments
  cart() { return this.get<CartView>('/cart'); }
  addToCart(itemType: string, itemId: string) { return this.post<unknown>('/cart/items', { itemType, itemId }); }
  removeFromCart(id: string) { return this.unwrap(firstValueFrom(this.http.delete<ApiResponse<unknown>>(`${this.base}/cart/items/${this.id(id)}`))); }
  wishlist() { return this.get<{ items: WishlistItem[] }>('/wishlist'); }
  addToWishlist(courseId: string) { return this.post<{ id: string }>(`/wishlist/${this.id(courseId)}`, {}); }
  removeFromWishlist(courseId: string) { return this.unwrap(firstValueFrom(this.http.delete<ApiResponse<unknown>>(`${this.base}/wishlist/${this.id(courseId)}`))); }
  previewCoupon(code: string) { return this.post<CouponPreview>('/cart/coupon', { code }); }
  checkout(paymentMethod: string, couponCode: string | null, voucherCode: string | null, gateway: string | null = null, referralCode: string | null = null) { return this.post<CheckoutOutcome>('/orders/checkout', { paymentMethod, couponCode, voucherCode, gateway, referralCode }); }
  wallet() { return this.get<WalletBalance>('/wallet'); }
  walletTransactions() { return this.get<Paged<WalletTx>>('/wallet/transactions'); }
  mySubscriptions() { return this.get<{ id: string; planName: string; price: number; currency: string; billingPeriod: string; status: string; startedAtUtc: string; currentPeriodEndUtc: string; cancelledAtUtc: string | null }[]>('/subscriptions/mine'); }
  cancelSubscription(id: string) { return this.post<unknown>(`/subscriptions/${this.id(id)}/cancel`); }
  redeemCourseVoucher(code: string) { return this.post<{ courseId: string; title: string }>('/vouchers/redeem', { code }); }
  recordStudySession(r: { courseId: string; lessonId: string | null; startedAtUtc: string; durationSeconds: number }) { return this.post<unknown>('/study-sessions', r); }
  redeemVoucherToWallet(code: string) { return this.post<{ credited: number; balance: number }>('/wallet/topup/voucher', { code }); }
  /** The API takes multipart/form-data with a receipt file (PNG, JPEG or PDF, up to 6 MB). */
  submitManualPayment(body: { purpose: string; method: string; amount: number; orderId: string | null; senderReference: string | null; senderPhoneOrAccount: string | null }, receipt: File) {
    const f = new FormData();
    f.append('purpose', body.purpose); f.append('method', body.method); f.append('amount', String(body.amount));
    if (body.orderId) f.append('orderId', body.orderId);
    if (body.senderReference) f.append('senderReference', body.senderReference);
    if (body.senderPhoneOrAccount) f.append('senderPhoneOrAccount', body.senderPhoneOrAccount);
    f.append('receipt', receipt, receipt.name);
    return this.post<{ id: string }>('/manual-payments', f);
  }
  topUpGateway(amount: number, gateway: string) { return this.post<{ orderId?: string; paymentId: string; checkoutUrl: string; reference: string }>('/wallet/topup/gateway', { amount, gateway }); }
  orders() { return this.get<Paged<OrderRow>>('/orders'); }
  myManualPayments() { return this.get<ManualPayment[]>('/manual-payments/mine'); }

  // AI (answers are grounded in indexed course material; the UI shows citations and the draft/advisory notices)
  tutor(courseId: string, question: string, language: string) { return this.post<AiTutorResult>(`/ai/courses/${this.id(courseId)}/tutor/chat`, { question, language }); }
  semanticSearch(courseId: string, query: string) { return this.post<{ hits: AiSearchHit[] }>(`/ai/courses/${this.id(courseId)}/video/semantic-search`, { query, topK: 8 }); }
  summarize(lessonId: string) { return this.post<AiSummary>(`/ai/lessons/${this.id(lessonId)}/summarize`); }
  mindMap(lessonId: string) { return this.post<AiMindMap>(`/ai/lessons/${this.id(lessonId)}/mindmap`); }
  studyPlan(courseId: string, targetDate: string, hoursPerWeek: number) { return this.post<AiStudyPlan>(`/ai/courses/${this.id(courseId)}/study-plan`, { targetDate, hoursPerWeek }); }
  voiceTutor(courseId: string, audio: Blob, language: string) {
    const f = new FormData(); f.append('audio', audio, 'question.webm'); f.append('language', language);
    return this.post<AiVoice>(`/ai/courses/${this.id(courseId)}/voice-tutor`, f);
  }
  generateQuiz(lessonId: string, mcqCount: number, openCount: number, flashcardCount: number) { return this.post<AiQuizDraft>(`/ai/lessons/${this.id(lessonId)}/generate-quiz`, { mcqCount, openEndedCount: openCount, flashcardCount }); }

  // instructor builder
  myCourses() { return this.get<MyCourse[]>('/courses/mine'); }
  createCourse(title: string, shortDescription: string) { return this.post<unknown>('/courses', { title, shortDescription }); }
  addSection(courseId: string, title: string) { return this.post<unknown>(`/courses/${this.id(courseId)}/sections`, { title }); }
  addLesson(sectionId: string, title: string, lessonType: string, contentMarkdown: string | null, externalUrl: string | null) {
    return this.post<unknown>(`/sections/${this.id(sectionId)}/lessons`, { title, lessonType, contentMarkdown, externalUrl, isPublished: true });
  }
  uploadLessonMedia(lessonId: string, file: File) { const f = new FormData(); f.append('file', file, file.name); return this.post<unknown>(`/lessons/${this.id(lessonId)}/media`, f); }
  // builder: edit, delete, reorder, chapters, resources
  private del(path: string) { return this.unwrap(firstValueFrom(this.http.delete<ApiResponse<unknown>>(`${this.base}${path}`))); }
  updateSection(id: string, title: string, description: string | null, sortOrder: number) { return this.put<unknown>(`/sections/${this.id(id)}`, { title, description, sortOrder }); }
  deleteSection(id: string) { return this.del(`/sections/${this.id(id)}`); }
  updateLesson(id: string, l: Lesson, title: string, contentMarkdown: string | null, externalUrl: string | null, sortOrder: number) {
    return this.put<unknown>(`/lessons/${this.id(id)}`, { title, lessonType: l.lessonType, contentMarkdown, externalUrl, durationSeconds: l.durationSeconds, sortOrder, isFreePreview: l.isFreePreview, isPublished: true });
  }
  deleteLesson(id: string) { return this.del(`/lessons/${this.id(id)}`); }
  orderSections(courseId: string, ids: string[]) { return this.put<unknown>(`/courses/${this.id(courseId)}/sections/order`, { ids }); }
  orderLessons(sectionId: string, ids: string[]) { return this.put<unknown>(`/sections/${this.id(sectionId)}/lessons/order`, { ids }); }
  setChapters(lessonId: string, chapters: { title: string; startSeconds: number }[]) { return this.put<unknown>(`/lessons/${this.id(lessonId)}/chapters`, { chapters }); }
  uploadResource(lessonId: string, file: File, title: string | null, isDownloadable: boolean) {
    const f = new FormData(); f.append('file', file, file.name); if (title) f.append('title', title); f.append('isDownloadable', String(isDownloadable));
    return this.post<unknown>(`/lessons/${this.id(lessonId)}/resources`, f);
  }
  // admin: manual payment review and voucher batches
  reviewQueue(status: string) { return this.get<Paged<ReviewItem>>('/manual-payments', new HttpParams().set('status', status)); }
  approveManual(id: string) { return this.post<unknown>(`/manual-payments/${this.id(id)}/approve`); }
  rejectManual(id: string, reason: string) { return this.post<unknown>(`/manual-payments/${this.id(id)}/reject`, { reason }); }
  receiptBlob(id: string) { return firstValueFrom(this.http.get(`${this.base}/manual-payments/${this.id(id)}/receipt`, { responseType: 'blob' })); }
  voucherBatches() { return this.get<Paged<VoucherBatch>>('/voucher-batches'); }
  createVoucherBatch(b: { name: string; prefix: string | null; quantity: number; voucherType: string; courseId: string | null; walletAmount: number | null; expiresAtUtc: string | null }) { return this.post<unknown>('/voucher-batches', b); }
  voidVoucherBatch(id: string) { return this.post<unknown>(`/voucher-batches/${this.id(id)}/void`); }
  voucherCsv(id: string) { return firstValueFrom(this.http.get(`${this.base}/voucher-batches/${this.id(id)}/export`, { responseType: 'blob' })); }
  // player extras
  notes(lessonId: string) { return this.get<LessonNote[]>(`/lessons/${this.id(lessonId)}/notes`); }
  addNote(lessonId: string, timestampSeconds: number | null, body: string) { return this.post<{ id: string }>(`/lessons/${this.id(lessonId)}/notes`, { timestampSeconds, body }); }
  editNote(id: string, timestampSeconds: number | null, body: string) { return this.put(`/notes/${this.id(id)}`, { timestampSeconds, body }); }
  deleteNote(id: string) { return this.del(`/notes/${this.id(id)}`); }
  bookmarks(lessonId: string) { return this.get<LessonBookmark[]>(`/lessons/${this.id(lessonId)}/bookmarks`); }
  addBookmark(lessonId: string, timestampSeconds: number, label: string | null) { return this.post<{ id: string }>(`/lessons/${this.id(lessonId)}/bookmarks`, { timestampSeconds, label }); }
  deleteBookmark(id: string) { return this.del(`/bookmarks/${this.id(id)}`); }
  chapters(lessonId: string) { return this.get<LessonChapter[]>(`/lessons/${this.id(lessonId)}/chapters`); }
  slides(lessonId: string) { return this.get<LessonSlide[]>(`/lessons/${this.id(lessonId)}/slides`); }
  resources(lessonId: string) { return this.get<LessonResource[]>(`/lessons/${this.id(lessonId)}/resources`); }
  resourceBlob(lessonId: string, resourceId: string) { return firstValueFrom(this.http.get(`${this.base}/lessons/${this.id(lessonId)}/resources/${this.id(resourceId)}/download`, { responseType: 'blob' })); }
  unlockLesson(lessonId: string) { return this.post<{ unlocked: boolean; charged: boolean }>(`/lessons/${this.id(lessonId)}/unlock`); }
  // orders, affiliate, bundles, plans
  invoiceBlob(orderId: string) { return firstValueFrom(this.http.get(`${this.base}/orders/${this.id(orderId)}/invoice`, { responseType: 'blob' })); }
  affiliate() { return this.get<AffiliateMe>('/affiliate/me'); }
  coupons() { return this.get<CouponDto[]>('/coupons'); }
  createCoupon(c: CouponInput) { return this.post<{ id: string }>('/coupons', c); }
  updateCoupon(id: string, c: CouponInput) { return this.put<unknown>(`/coupons/${id}`, c); }
  createBundle(b: { name: string; description: string | null; price: number; currency: string | null; courseIds: string[] }) { return this.post<{ id: string }>('/bundles', b); }
  createPlan(p: { name: string; description: string | null; price: number; currency: string | null; billingPeriod: string; scope: string; courseIds: string[] | null }) { return this.post<{ id: string }>('/subscription-plans', p); }
  createAffiliate(userId: string, commissionPercent: number, referralCode: string | null) { return this.post<unknown>('/affiliate/profiles', { userId, commissionPercent, referralCode }); }
  bundles() { return this.get<Bundle[]>('/bundles'); }
  plans() { return this.get<Plan[]>('/subscription-plans'); }
  subscribe(planId: string, paymentMethod: string, gateway: string | null) { return this.post<CheckoutOutcome>('/subscriptions', { planId, paymentMethod, gateway }); }
  // authoring: pricing, assessments, quiz points, slides
  setPricing(courseId: string, pricingModel: string, price: number, currency: string | null, accessDays: number | null) { return this.put<unknown>(`/courses/${this.id(courseId)}/pricing`, { pricingModel, price, currency, accessDays }); }
  setLessonPrice(lessonId: string, price: number | null) { return this.put<unknown>(`/lessons/${this.id(lessonId)}/price`, { price }); }
  questionBanks(courseId: string) { return this.get<QuestionBank[]>(`/courses/${this.id(courseId)}/question-banks`); }
  createBank(courseId: string, name: string, topic: string | null) { return this.post<{ id: string }>('/question-banks', { courseId, name, topic }); }
  questions(bankId: string) { return this.get<QuestionDto[]>(`/question-banks/${this.id(bankId)}/questions`); }
  createQuestion(bankId: string, q: QuestionInput) { return this.post<{ id: string }>(`/question-banks/${this.id(bankId)}/questions`, q); }
  deleteQuestion(id: string) { return this.del(`/questions/${this.id(id)}`); }
  createExam(courseId: string, e: ExamInput) { return this.post<{ id: string }>(`/courses/${this.id(courseId)}/exams`, e); }
  publishExam(id: string) { return this.post<unknown>(`/exams/${this.id(id)}/publish`); }
  /** Author-only exam settings (added backend route): the edit form prefills from this so it never overwrites fields it cannot see. */
  exam(id: string) { return this.get<ExamDetail>(`/exams/${this.id(id)}`); }
  updateExam(id: string, e: ExamInput) { return this.put<unknown>(`/exams/${this.id(id)}`, e); }
  examAttempts(examId: string) { return this.get<ExamAttemptRow[]>(`/exams/${this.id(examId)}/attempts`); }
  /** Staff grading view of one attempt: the same result route returns per-answer rows (with answerId) to graders. */
  attemptForGrading(id: string) { return this.get<GradingView>(`/attempts/${this.id(id)}/result`); }
  gradeAnswer(answerId: string, points: number, feedback: string | null) { return this.put<unknown>(`/attempt-answers/${this.id(answerId)}/grade`, { points, feedback }); }
  importQuestions(bankId: string, format: string, content: string) { return this.post<{ imported: number }>(`/question-banks/${this.id(bankId)}/import`, { format, content }); }
  // course staff and prerequisites (course owner or TenantAdmin)
  courseStaff(courseId: string) { return this.get<CourseStaffRow[]>(`/courses/${this.id(courseId)}/staff`); }
  setCourseStaff(courseId: string, staff: { userId: string; staffRole: string }[]) { return this.put<unknown>(`/courses/${this.id(courseId)}/staff`, { staff }); }
  /** Current prerequisites (added backend route); older backends 404 and the editor says an update is required. */
  prerequisites(courseId: string) { return this.get<{ courseId: string; title: string }[]>(`/courses/${this.id(courseId)}/prerequisites`); }
  setPrerequisites(courseId: string, courseIds: string[]) { return this.put<unknown>(`/courses/${this.id(courseId)}/prerequisites`, { courseIds }); }
  order(id: string) { return this.get<OrderDetail>(`/orders/${this.id(id)}`); }
  quizPoints(lessonId: string) { return this.get<QuizPointDto[]>(`/lessons/${this.id(lessonId)}/interactive-quizzes`); }
  setQuizPoints(lessonId: string, points: { questionId: string; timestampSeconds: number; isRequired: boolean; allowSkipAfterAttempts: number | null }[]) { return this.put<unknown>(`/lessons/${this.id(lessonId)}/interactive-quizzes`, { points }); }
  setSlides(lessonId: string, slides: unknown[]) { return this.put<unknown>(`/lessons/${this.id(lessonId)}/slides`, { slides }); }
  submitForApproval(courseId: string) { return this.post<unknown>(`/courses/${this.id(courseId)}/submit-for-approval`); }
  queueIngest(lessonId: string) { return this.post<unknown>(`/ai/lessons/${this.id(lessonId)}/ingest`); }
}
