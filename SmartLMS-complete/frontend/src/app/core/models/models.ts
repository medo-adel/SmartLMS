export type RoleName = 'SuperAdmin' | 'TenantAdmin' | 'Instructor' | 'TeachingAssistant' | 'Student' | 'Parent';
export type DashboardKind = 'student' | 'instructor' | 'admin' | 'parent' | 'none';

export interface User {
  id: string; tenantId: string | null; fullName: string; email: string | null; phoneNumber: string | null;
  preferredLanguage: string; isActive: boolean; emailConfirmed: boolean; roles: string[]; lastLoginAtUtc: string | null;
}
export interface TokenResponse {
  accessToken: string; accessTokenExpiresAtUtc: string; refreshToken: string; refreshTokenExpiresAtUtc: string; user: User;
}
export interface Session { accessToken: string; refreshToken: string; user: User; }
export interface FeatureFlag { key: string; isEnabled: boolean; }
export interface Branding {
  name: string; slug: string; logoUrl: string | null; primaryColor: string | null; secondaryColor: string | null;
  accentColor: string | null; defaultLanguage: string; featureFlags: FeatureFlag[];
}
export interface UserDevice { id: string; deviceType: string; platform: string | null; deviceName: string | null; firstSeenAtUtc: string; lastSeenAtUtc: string; isActive: boolean; }
export interface Tenant { id: string; name: string; slug: string; subdomain: string | null; logoUrl: string | null; primaryColor: string | null; secondaryColor: string | null; accentColor: string | null; defaultLanguage: string; timeZoneId: string; currency: string; maxMobileDevices: number; maxWebDevices: number; isActive: boolean; }
export interface CategoryItem { id: string; parentCategoryId: string | null; name: string; slug: string; sortOrder: number; iconName: string | null; }
export interface SupportAccess { tenantId: string; expiresAtUtc: string; }
export interface AppNotification { id: string; type: string; title: string; body: string | null; isRead: boolean; createdAtUtc: string; }

// ---- dashboards (shapes taken from AdminService.cs / EngagementServices.cs) ----
export interface StudentDashboard {
  enrollments: { courseId: string; title: string; status: string; progressPercent: number; expiresAtUtc: string | null }[];
  upcomingDeadlines: { id: string; courseId: string; title: string; dueAtUtc: string }[];
  xp: number; streak: number; longestStreak: number;
  skillRadar: { skill: string; averagePercent: number; attempts: number }[];
}
export interface InstructorDashboard {
  courses: number; totalStudents: number;
  enrollmentTrend: { weekStart: string; enrollments: number }[];
  lessonDropOff: { lessonId: string; courseId: string; title: string; started: number; completed: number; dropOffPercent: number }[];
  quizDifficulty: { questionId: string; text: string; answers: number; correctPercent: number }[];
  grossRevenue: number; revenueNote: string;
}
export interface AdminDashboard {
  kpis: { users: number; publishedCourses: number; activeEnrollments: number; revenueLast30Days: number; revenueAllTime: number; paidOrdersLast30Days: number };
  queues: { coursesAwaitingApproval: number; manualPaymentsToReview: number; payoutsToApprove: number; failedWebhookDeliveries: number };
  revenueByDay: { day: string; revenue: number; orders: number }[];
}
export interface ParentChild { studentId: string; fullName: string; }
export interface ParentSummary {
  studentId: string; courses: { title: string; status: string; progressPercent: number }[];
  averageExamScorePercent: number | null; weeklyStudyHours: number; attendancePercent: number | null; riskAlerts: string[];
}
