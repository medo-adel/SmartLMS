// Shapes taken from the .NET services (LmsService, AssessmentService, WalletService, CommerceService, AiService, IAiServiceClient).
export interface Paged<T> { totalCount: number; page: number; pageSize: number; items: T[]; }
export interface CourseListItem { id: string; title: string; slug: string; shortDescription: string | null; level: string; language: string | null; estimatedHours: number | null; pricingModel: string; price: number | null; currency: string | null; }
export interface LessonOutline { id: string; title: string; lessonType: string; durationSeconds: number | null; sortOrder: number; isFreePreview: boolean; }
export interface SectionOutline { id: string; title: string; description: string | null; sortOrder: number; lessons: LessonOutline[]; }
export interface CourseDetail extends CourseListItem { description: string | null; status: string; sections: SectionOutline[]; averageRating?: number | null; ratingsCount?: number; }
export interface Lesson { id: string; courseId: string; sectionId: string; title: string; lessonType: string; contentMarkdown: string | null; externalUrl: string | null; durationSeconds: number | null; isFreePreview: boolean; }
export interface Enrollment { id: string; courseId: string; title: string; status: string; progressPercent: number; expiresAtUtc: string | null; }
export interface CourseProgress { courseId: string; progressPercent: number; status: string; lessons: { lessonId: string; status: string; lastPositionSeconds: number; watchedSeconds: number }[]; }
export interface PathListItem { id: string; title: string; slug: string; description: string | null; }
export interface PathDetail { id: string; title: string; description: string | null; status: string; issuesCertificate: boolean; courses: { id: string; title: string; sortOrder: number; isMandatory: boolean }[]; }
export interface PathRequest { title: string; description: string | null; status: string | null; issuesCertificate: boolean; courseIds: string[] | null; }
export interface CartView { items: { id: string; itemType: string; itemId: string; description: string; price: number; currency: string; available: boolean }[]; subtotal: number; currency: string; }
export interface WishlistItem { id: string; courseId: string; title: string; slug: string; shortDescription: string | null; thumbnailFileId: string | null; pricingModel: string; price: number; currency: string; createdAtUtc: string; }
export interface CouponPreview { code: string; discount: number; subtotal: number; total: number; currency: string; }
export interface WalletBalance { balance: number; currency: string; }
export interface WalletTx { id: string; type: string; amount: number; balanceAfter: number; description: string | null; createdAtUtc: string; }
export interface ManualPayment { id: string; purpose: string; method: string; amount: number; status: string; rejectionReason: string | null; createdAtUtc: string; }
export interface ExamListItem { id: string; title: string; kind: string; durationMinutes: number | null; maxAttempts: number; passingScorePercent: number; isPublished?: boolean; }
export interface AttemptOption { id: string; text: string; }
export interface AttemptQuestion { attemptQuestionId: string; type: string; text: string; points: number; savedAnswer: AnswerValue | null; options: AttemptOption[] | null; matchTargets: string[] | null; }
export interface AttemptView { attemptId: string; expiresAtUtc: string | null; remainingSeconds: number | null; questions: AttemptQuestion[]; }
export interface AnswerValue { optionId?: string; optionIds?: string[]; text?: string; pairs?: Record<string, string>; }
export interface AttemptResult { attemptId: string; status: string; score: number | null; maxScore: number | null; passed: boolean | null; awaitingManualGrading: number; tabSwitchCount: number; }
export interface InteractiveQuiz { id: string; timestampSeconds: number; isRequired: boolean; question: { id: string; text: string; questionType: string }; options: AttemptOption[]; }
export interface AiCitation { lessonId: string; page: number | null; startSeconds: number | null; endSeconds: number | null; snippet: string; }
export interface AiTutorResult { answer: string; citations: AiCitation[]; grounded: boolean; }
export interface AiSearchHit { lessonId: string; startSeconds: number; endSeconds: number; snippet: string; score: number; }
export interface AiSummary { takeaways: string[]; keyTerms: string[]; chapters: { title: string; startSeconds: number }[]; }
export interface AiMindMap { nodes: { id: string; label: string }[]; edges: { source: string; target: string }[]; }
export interface AiVoice { transcript: string; answerText: string; audioBase64: string | null; audioMime: string | null; citations: AiCitation[]; }
export interface AiStudyPlan { days: { date: string; focus: string; minutes: number }[]; feasible: boolean; note: string | null; }
export interface AiQuizDraft { draft: boolean; notice: string; result: { mcqs: { question: string; options: string[]; correctIndex: number; explanation: string }[]; openEnded: { question: string; modelAnswer: string }[]; flashcards: { front: string; back: string }[] }; }
export interface MyCourse { id: string; title: string; status: string; ownerInstructorId: string; }

/** What POST /orders/checkout returns. The API sends an untyped object; these are the shapes CommerceService builds. */
export interface CheckoutOutcome { orderId: string; status: string; total: number; currency: string; paymentId?: string; checkoutUrl?: string; reference?: string; next?: string; }
export interface OrderRow { id: string; status: string; subtotal: number; discountTotal: number; total: number; currency: string; paymentMethod: string; createdAtUtc: string; paidAtUtc: string | null; }
export interface ReviewItem { id: string; userId: string; userName: string | null; purpose: string; method: string; amount: number; orderId: string | null; senderReference: string | null; senderPhoneOrAccount: string | null; status: string; createdAtUtc: string; }
export interface VoucherBatch { id: string; name: string; prefix: string | null; quantity: number; voucherType: string; courseId: string | null; walletAmount: number | null; expiresAtUtc: string | null; status: string; createdAtUtc: string; unused: number; redeemed: number; voided: number; }
export interface LessonNote { id: string; timestampSeconds: number | null; body: string; createdAtUtc: string; }
export interface LessonBookmark { id: string; timestampSeconds: number; label: string | null; }
export interface LessonChapter { id: string; title: string; startSeconds: number; source: string; }
export interface TranscriptCue { start: number; end: number; text: string; }
export interface LessonTranscript { cueCount: number; cues: TranscriptCue[]; generatedAtUtc: string; }
export interface LessonSlide { id: string; sortOrder: number; content: { title?: string; body?: string; imageUrl?: string; [k: string]: unknown }; }
export interface LessonResource { id: string; title: string; isDownloadable: boolean; contentType: string; sizeBytes: number; fileName: string; }
export interface Bundle { id: string; name: string; description: string | null; price: number; currency: string; courses: { id: string; title: string }[]; }
export interface Plan { id: string; name: string; description: string | null; price: number; currency: string; billingPeriod: string; scope: string; }
export interface AffiliateMe { referralCode: string; referralLink: string; commissionPercent: number; referrals: { referredUserId: string; name: string; createdAtUtc: string }[]; commissions: { id: string; orderId: string; amount: number; status: string; createdAtUtc: string }[]; totalCredited: number; }
export interface QuestionBank { id: string; name: string; topic: string | null; questionCount: number; }
export interface QuestionOptionDto { id: string; text: string; isCorrect: boolean; matchKey: string | null; }
export interface QuestionDto { id: string; questionType: string; text: string; points: number; difficulty: string; topic: string | null; explanation: string | null; options: QuestionOptionDto[]; }
export interface QuestionInput { questionType: string; text: string; points: number; difficulty: string | null; topic: string | null; explanation: string | null; trueFalseAnswer: boolean | null; options: { text: string; isCorrect: boolean; matchKey: string | null }[] | null; }
export interface ExamDetail extends ExamListItem { courseId: string; instructions: string | null; availableFromUtc: string | null; availableUntilUtc: string | null; tabSwitchDetection: boolean; maxTabSwitches: number | null; locked: boolean; }
export interface ExamAttemptRow { id: string; userId: string; attemptNumber: number; status: string; score: number | null; maxScore: number | null; passed: boolean | null; tabSwitchCount: number; }
export interface GradingAnswer { answerId: string; questionId: string; answerJson: unknown; isCorrect: boolean | null; pointsAwarded: number | null; feedback: string | null; }
export interface GradingView { attemptId: string; userId: string; status: string; score: number | null; maxScore: number | null; passed: boolean | null; awaitingManualGrading: number; tabSwitchCount: number; answers: GradingAnswer[] | null; }
export interface CourseStaffRow { userId: string; fullName: string; staffRole: string; }
export interface OrderDetail { id: string; userId: string; status: string; subtotal: number; discountTotal: number; total: number; currency: string; paymentMethod: string; createdAtUtc: string; paidAtUtc: string | null; items: { id: string; itemType: string; description: string; unitPrice: number; quantity: number; lineTotal: number }[]; payments: { id: string; method: string; status: string; amount: number; currency: string; createdAtUtc: string; confirmedAtUtc: string | null; failureReason: string | null }[]; invoiceNumber: string | null; }
export interface ExamInput { title: string; kind: string | null; instructions: string | null; durationMinutes: number | null; maxAttempts: number; passingScorePercent: number; questionIds: string[] | null; pickFromBankId: string | null; pickCount: number | null; availableFromUtc: string | null; availableUntilUtc: string | null; tabSwitchDetection: boolean; maxTabSwitches: number | null; }
export interface QuizPointDto { id: string; timestampSeconds: number; isRequired: boolean; allowSkipAfterAttempts: number | null; question: { id: string; text: string; questionType: string }; }
export interface CouponDto { id: string; code: string; discountType: string; value: number; maxRedemptions: number | null; maxPerUser: number | null; minOrderAmount: number | null; expiresAtUtc: string | null; courseId: string | null; isActive: boolean; redemptions: number; }
export interface CouponInput { code: string; discountType: string; value: number; maxRedemptions: number | null; maxPerUser: number | null; minOrderAmount: number | null; expiresAtUtc: string | null; courseId: string | null; isActive: boolean; }
