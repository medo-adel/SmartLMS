"""Single source of truth for the SmartLMS SQL Server schema.

Column grammar:  "Name type[?] [!] [>Table] [=default]"
  types: g guid | s:N nvarchar(N) | t nvarchar(max) | i int | bi bigint | b bit | m decimal(18,2)
         d datetime2 | dt date | f float | j JSON text (ISJSON check) | rv rowversion | e:A|B|C string enum (CHECK)
  ?  nullable    !  unique (single column)    >T  foreign key to T(Id)
Table flags: T tenant-owned (adds TenantId, indexed, global filter)  N nullable TenantId (platform-level rows)
             A auditable (CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy)  S soft-deletable (IsDeleted/DeletedAtUtc/DeletedBy)
"""

TABLES = []  # filled by T()


def T(name, group, cols, flags="TAS", uniq=(), idx=(), note=""):
    TABLES.append(dict(name=name, group=group, cols=cols, flags=flags, uniq=list(uniq), idx=list(idx), note=note))


# ---------------------------------------------------------------- Tenancy
G = "Tenancy"
T("Tenants", G, [
    "Name s:200", "Slug s:100!", "Subdomain s:100?!", "LogoUrl s:500?", "PrimaryColor s:9?", "SecondaryColor s:9?",
    "AccentColor s:9?", "DefaultLanguage s:10 ='en'", "TimeZoneId s:64 ='Africa/Cairo'", "Currency s:3 ='EGP'",
    "MaxMobileDevices i =1", "MaxWebDevices i =1", "IsActive b =1", "InstructorSharePercent f?", "SubscriptionMinWatchPercent f?", "SubscriptionMinAvgLessonSeconds i?", "SubscriptionJumpAlertPercent f?",
], flags="AS", note="One row per white-label customer. Not itself tenant-filtered.")
T("TenantFeatureFlags", G, ["FeatureKey s:100", "IsEnabled b =0"], flags="TA", uniq=[("TenantId", "FeatureKey")],
  note="Tenant-level feature toggles such as wallet, affiliates, live_classes, ai_tutor.")

# ---------------------------------------------------------------- Identity & auth
G = "Identity & Auth"
T("AspNetUsers", G, [
    "FullName s:200", "UserName s:256", "NormalizedUserName s:256", "Email s:256?", "NormalizedEmail s:256?",
    "EmailConfirmed b =0", "PasswordHash t?", "SecurityStamp t?", "ConcurrencyStamp t?", "PhoneNumber s:32?",
    "PhoneNumberConfirmed b =0", "TwoFactorEnabled b =0", "LockoutEnd o?", "LockoutEnabled b =1", "AccessFailedCount i =0",
    "AvatarUrl s:500?", "PreferredLanguage s:10 ='en'", "IsActive b =1", "LastLoginAtUtc d?", "BiometricLoginEnabled b =0",
], flags="NAS", uniq=[("TenantId", "NormalizedUserName")],
  note="ASP.NET Core Identity user (Guid key). TenantId is NULL only for SuperAdmin. UserName is stored as '<tenantSlug>:<email>' so the same email can exist in two tenants.")
T("AspNetRoles", G, ["Name s:256", "NormalizedName s:256!", "ConcurrencyStamp t?"], flags="",
  note="Global roles: SuperAdmin, TenantAdmin, Instructor, TeachingAssistant, Student, Parent.")
T("AspNetUserRoles", G, ["UserId g >AspNetUsers", "RoleId g >AspNetRoles"], flags="", uniq=[("UserId", "RoleId")])
T("AspNetRoleClaims", G, ["RoleId g >AspNetRoles", "ClaimType t?", "ClaimValue t?"], flags="")
T("AspNetUserTokens", G, ["UserId g >AspNetUsers", "LoginProvider s:128", "Name s:128", "Value t?"], flags="", note="ASP.NET Core Identity token store (authenticator keys, recovery codes). Composite primary key.")
T("AspNetUserClaims", G, ["UserId g >AspNetUsers", "ClaimType t?", "ClaimValue t?"], flags="")
T("AspNetUserLogins", G, ["LoginProvider s:128", "ProviderKey s:128", "ProviderDisplayName t?", "UserId g >AspNetUsers"], flags="",
  uniq=[("LoginProvider", "ProviderKey")], note="Social login hooks (Google, Facebook, Apple).")
T("UserTenantMemberships", G, [
    "UserId g >>AspNetUsers", "Kind e:Home|SupportAccess =Home", "GrantedBy g? >>AspNetUsers", "ExpiresAtUtc d?", "RevokedAtUtc d?",
], flags="TA", note="Tenant membership of a global identity. EVERY user-to-tenant-data reference points here as (TenantId, UserId), so a row can only reference a user who is a member of the same tenant. Each tenant user has one Home row; a SuperAdmin acts inside a tenant only through an explicit, audited SupportAccess row.")
T("RefreshTokens", G, [
    "UserId g >>AspNetUsers", "DeviceId g? >UserDevices", "TokenHash s:128!", "ExpiresAtUtc d", "RevokedAtUtc d?",
    "ReplacedByTokenHash s:128?", "CreatedByIp s:64?", "RevokedReason s:200?",
], flags="NA", note="Rotating refresh tokens. Only the SHA-256 hash is stored. Reuse of a revoked token revokes the whole chain.")
T("OtpCodes", G, [
    "UserId g? >>AspNetUsers", "Destination s:256", "Purpose e:EmailVerification|PhoneVerification|PasswordReset|Login",
    "CodeHash s:128", "ExpiresAtUtc d", "ConsumedAtUtc d?", "AttemptCount i =0",
], flags="NA", idx=[("Destination", "Purpose")], note="Short-lived OTPs. Hash only; max attempts enforced in code.")

# ---------------------------------------------------------------- Anti-piracy
G = "Anti-Piracy"
T("UserDevices", G, [
    "UserId g >>AspNetUsers", "DeviceFingerprintHash s:128", "DeviceType e:Mobile|Web", "Platform s:30?", "DeviceName s:200?",
    "UserAgent s:500?", "FirstSeenAtUtc d", "LastSeenAtUtc d", "LastIp s:64?", "IsActive b =1", "RevokedAtUtc d?", "RevokedBy g? >>AspNetUsers",
], flags="NA", uniq=[("UserId", "DeviceFingerprintHash")],
  note="Device binding. Active devices per user and DeviceType are capped by Tenants.MaxMobileDevices / MaxWebDevices.")
T("DeviceResetLogs", G, [
    "UserId g >AspNetUsers", "ResetByUserId g >AspNetUsers", "DeviceId g? >UserDevices", "Reason s:500?", "ResetAtUtc d",
], flags="TA", note="Who reset which student's devices and when (also written to AuditLogs).")
T("PlaybackSessions", G, [
    "UserId g >AspNetUsers", "LessonId g >Lessons", "DeviceId g? >UserDevices", "StartedAtUtc d", "LastHeartbeatAtUtc d", "EndedAtUtc d?", "Ip s:64?",
], flags="T", idx=[("UserId", "LessonId")],
  note="Heartbeat log used to detect concurrent playback of one account on many devices. Detection signal, not a block by itself.")

# ---------------------------------------------------------------- Files
G = "Files & Jobs"
T("FileAssets", G, [
    "OriginalName s:260", "StoragePath s:500", "ContentType s:100", "SizeBytes bi", "Sha256 s:64?", "MeasuredDurationSeconds i?", "UploadedBy g? >AspNetUsers",
    "Purpose e:CourseMedia|LessonResource|Receipt|Submission|Certificate|Invoice|Avatar|Export|Other",
], flags="TAS", idx=[("Sha256",)], note="Metadata for every stored file. Bytes live on disk or object storage, never in SQL.")
T("BackgroundJobs", G, [
    "Type e:AiIngest|AiTranscribe|PlagiarismBatch|VoucherGeneration|WebhookDelivery|CertificatePdf|AnalyticsRollup|Email|Export|PeerReviewAssign|PayoutCalculation",
    "Status e:Pending|Processing|Completed|Failed", "HangfireJobId s:64?", "ReferenceType s:60?", "ReferenceId g?",
    "RequestedBy g? >AspNetUsers", "ProgressPercent i =0", "ErrorMessage s:2000?", "ResultJson j?", "StartedAtUtc d?", "CompletedAtUtc d?",
], flags="TA", idx=[("Status", "Type")], note="Application-level job status mirrored to clients over SignalR. Hangfire keeps its own [HangFire] schema.")

# ---------------------------------------------------------------- Catalog
G = "Course Catalog"
T("Categories", G, ["ParentCategoryId g? >Categories", "Name s:200", "Slug s:200", "SortOrder i =0", "IconName s:60?"],
  uniq=[("TenantId", "Slug")])
T("Courses", G, [
    "CategoryId g? >Categories", "OwnerInstructorId g >AspNetUsers", "Title s:250", "Slug s:250", "ShortDescription s:500?", "Description t?",
    "ThumbnailFileId g? >FileAssets", "PromoVideoUrl s:500?", "Level e:Beginner|Intermediate|Advanced|AllLevels", "Language s:10 ='en'",
    "Status e:Draft|PendingApproval|Published|Rejected|Archived =Draft",
    "PricingModel e:Free|OneTime|PayPerLesson|SubscriptionOnly =Free", "Price m =0", "Currency s:3 ='EGP'",
    "DripMode e:None|ByDate|DaysAfterEnrollment =None", "AccessDays i?", "EstimatedHours f?", "PublishedAtUtc d?",
], uniq=[("TenantId", "Slug")], idx=[("TenantId", "Status")])
T("CourseStaff", G, ["CourseId g >Courses", "UserId g >AspNetUsers", "StaffRole e:CoInstructor|TeachingAssistant"], flags="TA",
  uniq=[("CourseId", "UserId")])
T("CourseApprovals", G, [
    "CourseId g >Courses", "SubmittedBy g >AspNetUsers", "SubmittedAtUtc d", "ReviewedBy g? >AspNetUsers", "ReviewedAtUtc d?",
    "Decision e:Pending|Approved|Rejected =Pending", "Comment s:1000?",
], flags="TA", note="Instructor course approval workflow history.")
T("CoursePrerequisites", G, ["CourseId g >Courses", "PrerequisiteCourseId g >Courses"], flags="TA", uniq=[("CourseId", "PrerequisiteCourseId")])
T("Sections", G, [
    "CourseId g >Courses", "Title s:250", "Description s:1000?", "SortOrder i =0", "DripUnlockAtUtc d?", "DripDaysAfterEnrollment i?",
], idx=[("CourseId", "SortOrder")])
T("Lessons", G, [
    "CourseId g >Courses", "SectionId g >Sections", "Title s:250", "LessonType e:Video|Article|Pdf|Audio|ExternalLink|InteractiveSlides",
    "ContentMarkdown t?", "ContentFileId g? >FileAssets", "ExternalUrl s:1000?", "DurationSeconds i?", "SortOrder i =0",
    "IsFreePreview b =0", "PayPerLessonPrice m?", "PrerequisiteLessonId g? >Lessons", "DripUnlockAtUtc d?", "DripDaysAfterEnrollment i?", "IsPublished b =0",
], idx=[("SectionId", "SortOrder")], note="Video files are stored as FileAssets; streaming and DRM strategy is a Phase 3 decision (see docs/SECURITY_LIMITS.md).")
T("LessonResources", G, ["LessonId g >Lessons", "Title s:250", "FileId g >FileAssets", "IsDownloadable b =1"], flags="TA")
T("LessonChapters", G, ["LessonId g >Lessons", "Title s:250", "StartSeconds i", "Source e:Manual|Ai =Manual"], flags="TA")
T("LessonSlides", G, ["LessonId g >Lessons", "SortOrder i", "ContentJson j"], flags="TA", note="Interactive slide deck content.")
T("InteractiveVideoQuizzes", G, [
    "LessonId g >Lessons", "QuestionId g >Questions", "TimestampSeconds i", "IsRequired b =1", "AllowSkipAfterAttempts i?",
], flags="TA", idx=[("LessonId", "TimestampSeconds")], note="Pauses the player at TimestampSeconds until the question is answered.")
T("LearningPaths", G, [
    "Title s:250", "Slug s:250", "Description t?", "ThumbnailFileId g? >FileAssets", "Status e:Draft|Published|Archived =Draft",
    "Price m =0", "Currency s:3 ='EGP'", "IssuesCertificate b =1",
], uniq=[("TenantId", "Slug")])
T("LearningPathCourses", G, ["LearningPathId g >LearningPaths", "CourseId g >Courses", "SortOrder i", "IsMandatory b =1"], flags="TA",
  uniq=[("LearningPathId", "CourseId"), ("LearningPathId", "SortOrder")], note="Mandatory sequential ordering is enforced in the application layer.")

# ---------------------------------------------------------------- Learning
G = "Learning Progress"
T("Enrollments", G, [
    "UserId g >AspNetUsers", "CourseId g >Courses", "Source e:Free|Purchase|Voucher|Wallet|Subscription|Admin|Path",
    "Status e:Active|Completed|Expired|Revoked =Active", "EnrolledAtUtc d", "ExpiresAtUtc d?", "CompletedAtUtc d?", "ProgressPercent f =0",
    "OrderItemId g? >OrderItems",
], uniq=[("UserId", "CourseId")], idx=[("CourseId", "Status")])
T("LearningPathEnrollments", G, [
    "UserId g >AspNetUsers", "LearningPathId g >LearningPaths", "Status e:Active|Completed|Revoked =Active", "EnrolledAtUtc d", "CompletedAtUtc d?",
], uniq=[("UserId", "LearningPathId")])
T("LessonAccessGrants", G, [
    "UserId g >AspNetUsers", "LessonId g >Lessons", "Source e:PayPerLesson|Voucher|Wallet|Admin", "UnlockedAtUtc d", "ExpiresAtUtc d?", "OrderItemId g? >OrderItems",
], flags="TA", uniq=[("UserId", "LessonId")], note="Pay-per-lesson unlocks without a course enrollment.")
T("LessonProgress", G, [
    "UserId g >AspNetUsers", "LessonId g >Lessons", "CourseId g >Courses", "Status e:NotStarted|InProgress|Completed =NotStarted",
    "LastPositionSeconds i =0", "WatchedSeconds i =0", "PlaybackSpeed f?", "CompletedAtUtc d?", "LastAccessedAtUtc d",
], flags="TA", uniq=[("UserId", "LessonId")], idx=[("CourseId", "UserId")])
T("LessonNotes", G, ["UserId g >AspNetUsers", "LessonId g >Lessons", "TimestampSeconds i?", "Body s:4000"], flags="TAS")
T("LessonBookmarks", G, ["UserId g >AspNetUsers", "LessonId g >Lessons", "TimestampSeconds i", "Label s:200?"], flags="TAS")
T("InteractiveQuizResponses", G, [
    "UserId g >AspNetUsers", "InteractiveVideoQuizId g >InteractiveVideoQuizzes", "AnswerJson j", "IsCorrect b", "AnsweredAtUtc d",
], flags="TA")
T("StudySessions", G, [
    "UserId g >AspNetUsers", "CourseId g? >Courses", "LessonId g? >Lessons", "StartedAtUtc d", "DurationSeconds i",
], flags="T", idx=[("UserId", "StartedAtUtc")], note="Feeds weekly study hours (parent portal), streaks and risk scoring.")

# ---------------------------------------------------------------- Assessments
G = "Assessments"
T("QuestionBanks", G, ["CourseId g? >Courses", "Name s:250", "Topic s:200?", "Description s:1000?"])
T("Questions", G, [
    "QuestionBankId g >QuestionBanks", "QuestionType e:McqSingle|McqMulti|TrueFalse|FillBlank|Matching|ShortAnswer|Essay",
    "Text t", "Difficulty e:Easy|Medium|Hard =Medium", "Topic s:200?", "DefaultPoints f =1", "Explanation t?",
    "DataJson j?", "Source e:Manual|Ai =Manual",
], idx=[("QuestionBankId", "Difficulty")], note="DataJson holds accepted answers for FillBlank/ShortAnswer, pairs for Matching, and an optional rubric for Essay.")
T("QuestionOptions", G, ["QuestionId g >Questions", "Text t", "IsCorrect b =0", "SortOrder i =0", "MatchKey s:100?"], flags="TA")
T("Exams", G, [
    "CourseId g >Courses", "SectionId g? >Sections", "Title s:250", "Kind e:Quiz|Exam", "Instructions t?", "DurationMinutes i?",
    "MaxAttempts i =1", "PassingScorePercent f =50", "ShuffleQuestions b =0", "ShuffleOptions b =0",
    "AvailableFromUtc d?", "AvailableUntilUtc d?", "TabSwitchDetection b =0", "MaxTabSwitches i?", "RequirePrivacyScreen b =0", "IsPublished b =0",
])
T("ExamQuestionRules", G, [
    "ExamId g >Exams", "QuestionId g? >Questions", "QuestionBankId g? >QuestionBanks", "PickCount i?", "Difficulty e:Easy|Medium|Hard?",
    "Topic s:200?", "Points f?", "SortOrder i =0",
], flags="TA", note="Either a fixed question (QuestionId) or a random pool (QuestionBankId + PickCount + filters).")
T("ExamAttempts", G, [
    "ExamId g >Exams", "UserId g >AspNetUsers", "AttemptNumber i", "StartedAtUtc d", "ExpiresAtUtc d?", "SubmittedAtUtc d?",
    "Status e:InProgress|Submitted|Graded|Expired|Voided =InProgress", "Score f?", "MaxScore f?", "Passed b?", "TabSwitchCount i =0", "DeviceId g? >UserDevices",
], flags="TA", uniq=[("ExamId", "UserId", "AttemptNumber")])
T("ExamAttemptQuestions", G, ["ExamAttemptId g >ExamAttempts", "QuestionId g >Questions", "SortOrder i", "Points f"], flags="TA",
  note="Frozen set and order of questions for one attempt.")
T("ExamAnswers", G, [
    "ExamAttemptQuestionId g >ExamAttemptQuestions", "AnswerJson j?", "IsCorrect b?", "PointsAwarded f?", "GradedBy g? >AspNetUsers", "GradedAtUtc d?", "Feedback s:2000?",
], flags="TA", uniq=[("ExamAttemptQuestionId",)])
T("ProctoringEvents", G, [
    "ExamAttemptId g >ExamAttempts", "EventType e:TabSwitch|WindowBlur|FullscreenExit|CopyPaste|NetworkLoss|Other", "OccurredAtUtc d", "DetailsJson j?",
], flags="T", idx=[("ExamAttemptId", "OccurredAtUtc")], note="Signals for human review. Not proof of cheating (see docs/SECURITY_LIMITS.md).")

G = "Assignments"
T("Assignments", G, [
    "CourseId g >Courses", "LessonId g? >Lessons", "Title s:250", "Instructions t?", "DueAtUtc d?", "MaxPoints f", "AllowResubmission b =0",
    "MaxResubmissions i?", "IsGroup b =0", "MaxGroupSize i?", "PeerReviewEnabled b =0", "PeerReviewsPerSubmission i?", "PeerReviewDueAtUtc d?",
    "PlagiarismCheckEnabled b =0", "AiProvisionalGradingEnabled b =0", "IsPublished b =0",
])
T("AssignmentRubricCriteria", G, ["AssignmentId g >Assignments", "Name s:200", "Description s:1000?", "MaxPoints f", "SortOrder i =0"], flags="TA")
T("AssignmentGroups", G, ["AssignmentId g >Assignments", "Name s:200"], flags="TA")
T("AssignmentGroupMembers", G, ["AssignmentGroupId g >AssignmentGroups", "UserId g >AspNetUsers"], flags="TA", uniq=[("AssignmentGroupId", "UserId")])
T("Submissions", G, [
    "AssignmentId g >Assignments", "UserId g? >AspNetUsers", "AssignmentGroupId g? >AssignmentGroups", "Version i =1", "TextContent t?", "LinkUrl s:1000?",
    "SubmittedAtUtc d", "IsLate b =0", "Status e:Submitted|UnderReview|Graded|ReturnedForResubmission =Submitted", "TotalScore f?", "GradedBy g? >AspNetUsers",
    "GradedAtUtc d?", "InstructorFeedback t?", "AiProvisionalScore f?", "AiFeedback t?", "MaxSimilarity f?",
], note="Exactly one of UserId (individual) or AssignmentGroupId (team) is set. Enforced by CHECK. Unique version per submitter via two filtered indexes.")
T("SubmissionFiles", G, ["SubmissionId g >Submissions", "FileId g >FileAssets"], flags="TA")
T("SubmissionCriterionScores", G, ["SubmissionId g >Submissions", "CriterionId g >AssignmentRubricCriteria", "Points f", "Feedback s:2000?"], flags="TA",
  uniq=[("SubmissionId", "CriterionId")])
T("SubmissionComments", G, ["SubmissionId g >Submissions", "AuthorId g >AspNetUsers", "AnchorJson j?", "Body s:4000"], flags="TAS",
  note="Inline instructor feedback anchored to a text range or file page via AnchorJson.")
T("PeerReviews", G, [
    "SubmissionId g >Submissions", "ReviewerId g >AspNetUsers", "Status e:Assigned|Submitted|Skipped =Assigned", "TotalScore f?", "Comments s:4000?", "SubmittedAtUtc d?",
], flags="TA", uniq=[("SubmissionId", "ReviewerId")])
T("PeerReviewScores", G, ["PeerReviewId g >PeerReviews", "CriterionId g >AssignmentRubricCriteria", "Points f", "Comment s:1000?"], flags="TA")
T("PlagiarismChecks", G, [
    "SubmissionId g >Submissions", "BackgroundJobId g? >BackgroundJobs", "Status e:Pending|Processing|Completed|Failed =Pending", "MaxSimilarity f?", "CheckedAtUtc d?",
], flags="TA")
T("PlagiarismMatches", G, ["PlagiarismCheckId g >PlagiarismChecks", "OtherSubmissionId g >Submissions", "Similarity f", "ReviewedBy g? >AspNetUsers",
    "ReviewOutcome e:Unreviewed|Dismissed|Confirmed =Unreviewed"], flags="TA", note="Similarity is a signal; an instructor sets the outcome.")

# ---------------------------------------------------------------- Community
G = "Community & Live"
T("DiscussionThreads", G, [
    "CourseId g >Courses", "LessonId g? >Lessons", "AuthorId g >AspNetUsers", "ThreadType e:LessonQA|Forum", "Title s:300", "Body t", "IsPinned b =0",
    "IsLocked b =0", "UpvoteCount i =0", "AcceptedPostId g?",
], idx=[("CourseId", "LessonId")])
T("DiscussionPosts", G, [
    "ThreadId g >DiscussionThreads", "AuthorId g >AspNetUsers", "ParentPostId g? >DiscussionPosts", "Body t", "IsInstructorPinned b =0", "UpvoteCount i =0",
])
T("DiscussionVotes", G, ["UserId g >AspNetUsers", "ThreadId g? >DiscussionThreads", "PostId g? >DiscussionPosts"], flags="TA",
  note="CHECK: exactly one of ThreadId / PostId. Unique per user and target.")
T("CourseReviews", G, [
    "UserId g >AspNetUsers", "CourseId g >Courses", "Rating i", "Body s:2000?", "Status e:Published|Hidden =Published",
], flags="TA", uniq=[("TenantId", "UserId", "CourseId")], note="One review per student per course; only enrolled students may post. CHECK: Rating between 1 and 5. Hidden = removed by moderation.")
T("CourseReviewReports", G, [
    "ReviewId g >CourseReviews", "UserId g >AspNetUsers", "Reason s:1000", "Status e:Open|Dismissed|Actioned =Open",
], flags="TA", uniq=[("TenantId", "ReviewId", "UserId")], note="A user can report a review once; moderation resolves open reports.")
T("ChatRooms", G, ["CourseId g? >Courses", "RoomType e:Course|Direct|Group", "Name s:200?"], flags="TA")
T("ChatRoomMembers", G, ["ChatRoomId g >ChatRooms", "UserId g >AspNetUsers", "JoinedAtUtc d", "LastReadAtUtc d?"], flags="TA", uniq=[("ChatRoomId", "UserId")])
T("ChatMessages", G, ["ChatRoomId g >ChatRooms", "SenderId g >AspNetUsers", "Body s:4000", "SentAtUtc d"], flags="TS", idx=[("ChatRoomId", "SentAtUtc")])
T("Announcements", G, ["CourseId g? >Courses", "AuthorId g >AspNetUsers", "Title s:250", "Body t", "PublishedAtUtc d"])
T("Notifications", G, [
    "UserId g >AspNetUsers", "Type s:60", "Title s:250", "Body s:2000?", "DataJson j?", "IsRead b =0", "ReadAtUtc d?", "CreatedAtUtc d",
], flags="T", idx=[("UserId", "IsRead")])
T("PushTokens", G, ["UserId g >AspNetUsers", "DeviceId g? >UserDevices", "FcmToken s:500", "Platform e:Android|Ios|Web", "LastSeenAtUtc d"], flags="TA", uniq=[("UserId", "FcmToken")])
T("LiveSessions", G, [
    "CourseId g >Courses", "HostId g >AspNetUsers", "Title s:250", "Provider e:Zoom|GoogleMeet|Jitsi|Other", "JoinUrl s:1000", "StartsAtUtc d", "EndsAtUtc d?",
    "Status e:Scheduled|Live|Ended|Cancelled =Scheduled",
])
T("LiveSessionAttendance", G, ["LiveSessionId g >LiveSessions", "UserId g >AspNetUsers", "JoinedAtUtc d", "LeftAtUtc d?", "DurationSeconds i?", "Source e:JoinLink|Provider|Manual =JoinLink"],
  flags="TA", uniq=[("LiveSessionId", "UserId")], note="JoinLink attendance means the student opened our join link, not that they stayed engaged.")

# ---------------------------------------------------------------- Gamification & parents & certificates
G = "Gamification, Parents & Certificates"
T("XpTransactions", G, ["UserId g >AspNetUsers", "Points i", "Reason s:100", "SourceType s:60?", "SourceId g?", "AwardedAtUtc d"], flags="T", idx=[("UserId", "AwardedAtUtc")],
  note="Append-only. Leaderboards (course and global) are queries over this table, cached by the analytics job.")
T("UserStreaks", G, ["UserId g >AspNetUsers", "CurrentStreakDays i =0", "LongestStreakDays i =0", "LastActivityDate dt?"], flags="TA", uniq=[("UserId",)])
T("Badges", G, ["Code s:60", "Name s:200", "Description s:500?", "IconUrl s:500?", "RuleJson j?"], uniq=[("TenantId", "Code")])
T("UserBadges", G, ["UserId g >AspNetUsers", "BadgeId g >Badges", "AwardedAtUtc d"], flags="TA", uniq=[("UserId", "BadgeId")])
T("ParentStudentLinks", G, [
    "ParentUserId g >AspNetUsers", "StudentUserId g >AspNetUsers", "Status e:Pending|Active|Revoked =Pending", "LinkCodeHash s:128?", "ApprovedBy g? >AspNetUsers", "ApprovedAtUtc d?",
], flags="TA", uniq=[("ParentUserId", "StudentUserId")], note="A parent sees a student only while Status = Active. Approval by the student, an instructor or an admin is a tenant policy.")
T("ParentAlerts", G, [
    "ParentUserId g >AspNetUsers", "StudentUserId g >AspNetUsers", "AlertType e:LowQuizScore|LowAttendance|Inactivity|OverdueAssignment|HighDropoutRisk", "Message s:1000",
    "CreatedAtUtc d", "ReadAtUtc d?",
], flags="T", idx=[("ParentUserId", "ReadAtUtc")])
T("Certificates", G, [
    "UserId g >AspNetUsers", "CourseId g? >Courses", "LearningPathId g? >LearningPaths", "VerificationCode s:32!", "IssuedAtUtc d", "PdfFileId g? >FileAssets",
    "RevokedAtUtc d?", "RevokedReason s:500?",
], note="CHECK: exactly one of CourseId / LearningPathId. VerificationCode is globally unique for the public verify endpoint.")

# ---------------------------------------------------------------- Commerce
G = "Commerce"
T("Bundles", G, ["Name s:250", "Description t?", "Price m", "Currency s:3 ='EGP'", "IsActive b =1"])
T("BundleCourses", G, ["BundleId g >Bundles", "CourseId g >Courses"], flags="TA", uniq=[("BundleId", "CourseId")])
T("SubscriptionPlans", G, ["Name s:200", "Description s:1000?", "Price m", "Currency s:3 ='EGP'", "BillingPeriod e:Monthly|Quarterly|Yearly",
    "Scope e:AllCourses|SelectedCourses", "IsActive b =1"])
T("SubscriptionPlanCourses", G, ["PlanId g >SubscriptionPlans", "CourseId g >Courses"], flags="TA", uniq=[("PlanId", "CourseId")])
T("UserSubscriptions", G, [
    "UserId g >AspNetUsers", "PlanId g >SubscriptionPlans", "Status e:Active|PastDue|Cancelled|Expired", "StartedAtUtc d", "CurrentPeriodEndUtc d", "CancelledAtUtc d?", "GatewayReference s:200?",
], flags="TA", idx=[("UserId", "Status")])
T("CartItems", G, [
    "UserId g >AspNetUsers", "ItemType e:Course|Lesson|Bundle|Plan|Path", "CourseId g? >Courses", "LessonId g? >Lessons", "BundleId g? >Bundles",
    "PlanId g? >SubscriptionPlans", "LearningPathId g? >LearningPaths",
], flags="TA", note="CHECK: exactly one target column matches ItemType.")
T("WishlistItems", G, [
    "UserId g >AspNetUsers", "CourseId g >Courses",
], flags="TA", uniq=[("TenantId", "UserId", "CourseId")], note="Student wishlist: courses saved for later, one row per student-course pair.")
T("Coupons", G, [
    "Code s:50", "DiscountType e:Percentage|Fixed", "Value m", "MaxRedemptions i?", "MaxPerUser i =1", "MinOrderAmount m?", "ExpiresAtUtc d?", "CourseId g? >Courses", "IsActive b =1",
], uniq=[("TenantId", "Code")])
T("Orders", G, [
    "UserId g >AspNetUsers", "Status e:Pending|AwaitingManualReview|Paid|Failed|Cancelled|Refunded =Pending", "Subtotal m", "DiscountTotal m =0", "Total m", "Currency s:3",
    "CouponId g? >Coupons", "PaymentMethod e:Gateway|Wallet|Voucher|Manual|Free", "PaidAtUtc d?", "ReferralCode s:50?",
], idx=[("UserId", "Status")])
T("OrderItems", G, [
    "OrderId g >Orders", "ItemType e:Course|Lesson|Bundle|Plan|Path|WalletTopUp", "CourseId g? >Courses", "LessonId g? >Lessons", "BundleId g? >Bundles",
    "PlanId g? >SubscriptionPlans", "LearningPathId g? >LearningPaths", "Description s:300", "UnitPrice m", "Quantity i =1", "LineTotal m",
], flags="TA")
T("CouponRedemptions", G, ["CouponId g >Coupons", "UserId g >AspNetUsers", "OrderId g >Orders", "RedeemedAtUtc d"], flags="TA", uniq=[("CouponId", "OrderId")])
T("Payments", G, [
    "OrderId g >Orders", "Method e:Paymob|Fawry|Stripe|Wallet|Manual|Voucher", "GatewayReference s:200?", "Amount m", "Currency s:3", "Status e:Pending|Succeeded|Failed|Refunded",
    "FailureReason s:500?", "CreatedAtUtc d", "ConfirmedAtUtc d?", "IdempotencyKey s:100!",
], flags="T", idx=[("GatewayReference",)], note="Raw gateway payloads are not stored (they can carry card data). Store only reference and status.")
T("ManualPaymentSubmissions", G, [
    "UserId g >AspNetUsers", "OrderId g? >Orders", "Purpose e:Order|WalletTopUp", "Method e:VodafoneCash|InstaPay|BankTransfer", "Amount m", "SenderReference s:200?",
    "SenderPhoneOrAccount s:100?", "TransferredAtUtc d?", "ReceiptFileId g >FileAssets", "Status e:Pending|Approved|Rejected =Pending", "ReviewedBy g? >AspNetUsers",
    "ReviewedAtUtc d?", "RejectionReason s:500?", "WalletTransactionId g? >WalletTransactions",
], idx=[("Status", "TenantId")], note="Admin review queue. Approval writes the order/wallet change and an audit row in one transaction.")
T("Invoices", G, ["OrderId g >Orders", "InvoiceNumber s:40", "IssuedAtUtc d", "PdfFileId g? >FileAssets"], uniq=[("TenantId", "InvoiceNumber")])
T("VoucherBatches", G, [
    "Name s:200", "Prefix s:12?", "Quantity i", "VoucherType e:CourseAccess|WalletCredit", "CourseId g? >Courses", "WalletAmount m?", "ExpiresAtUtc d?",
    "BackgroundJobId g? >BackgroundJobs", "ExportFileId g? >FileAssets", "Status e:Pending|Generating|Ready|Failed =Pending",
])
T("Vouchers", G, [
    "VoucherBatchId g >VoucherBatches", "CodeHash s:64", "CodeLast4 s:4", "Status e:Unused|Redeemed|Void =Unused", "RedeemedBy g? >AspNetUsers", "RedeemedAtUtc d?",
], flags="TA", uniq=[("TenantId", "CodeHash")], idx=[("VoucherBatchId", "Status")],
  note="Only the SHA-256 of the code is stored. Plain codes exist once, in the CSV produced when the batch is generated.")
T("Wallets", G, ["UserId g >AspNetUsers", "Balance m =0", "Currency s:3 ='EGP'", "RowVersion rv"], flags="TA", uniq=[("UserId",)])
T("WalletTransactions", G, [
    "WalletId g >Wallets", "Type e:TopUpGateway|TopUpVoucher|TopUpManual|Purchase|Refund|AffiliateCommission|Adjustment", "Amount m", "BalanceAfter m", "ReferenceType s:60?",
    "ReferenceId g?", "Description s:300?", "IdempotencyKey s:100", "CreatedAtUtc d", "CreatedBy g? >AspNetUsers",
], flags="T", uniq=[("WalletId", "IdempotencyKey")], idx=[("WalletId", "CreatedAtUtc")],
  note="Append-only ledger. Amount is signed. Balance changes only through this table, inside a transaction with the wallet row's RowVersion check.")
T("AffiliateProfiles", G, ["UserId g >AspNetUsers", "ReferralCode s:40", "CommissionPercent f", "IsActive b =1"], uniq=[("TenantId", "ReferralCode"), ("UserId",)])
T("Referrals", G, ["AffiliateUserId g >AspNetUsers", "ReferredUserId g >AspNetUsers", "ReferralCodeUsed s:40"], flags="TA", uniq=[("ReferredUserId",)])
T("AffiliateCommissions", G, [
    "AffiliateUserId g >AspNetUsers", "OrderId g >Orders", "Amount m", "Status e:Pending|Credited|Reversed =Pending", "WalletTransactionId g? >WalletTransactions",
], flags="TA", uniq=[("AffiliateUserId", "OrderId")])
T("InstructorRevenueShares", G, ["CourseId g >Courses", "InstructorId g >AspNetUsers", "SharePercent f"], flags="TA", uniq=[("CourseId", "InstructorId")])
T("CourseLearningWeights", G, ["CourseId g >Courses", "NonVideoCourseSeconds i"], flags="TA", uniq=[("CourseId",)], note="TenantAdmin-approved learning time of ALL non-video content of a course together (split over its non-video lessons), used only for subscription revenue allocation.")
T("OrderItemAllocations", G, ["OrderItemId g >OrderItems", "CourseId g >Courses", "ListPrice m", "NonVideoSeconds i?", "NonVideoLessonCount i?"], flags="TA", uniq=[("OrderItemId", "CourseId")], note="Snapshot taken at checkout for each course covered by the line: its list price (bundle and path weights) and, for subscription plans, the approved non-video learning time and the published non-video lesson count at purchase.")
T("OrderItemShares", G, ["OrderItemId g >OrderItems", "CourseId g >Courses", "InstructorId g >AspNetUsers", "SharePercent f?", "ReleasedLate b =0", "SettledPayoutId g? >InstructorPayouts"], flags="TA", uniq=[("OrderItemId", "CourseId", "InstructorId")], note="Snapshot taken at checkout: which instructors earn from each covered course and at what percentage. A null percentage means none was configured at purchase and the current setting is used when earnings are calculated.")
T("InstructorPayouts", G, [
    "InstructorId g >AspNetUsers", "PeriodStartUtc d", "PeriodEndUtc d", "Amount m", "Currency s:3", "Status e:Calculated|Approved|Paid|Cancelled =Calculated",
    "PaidAtUtc d?", "PaymentReference s:200?", "ApprovedBy g? >AspNetUsers",
], flags="TA", note="Payout is recorded here; actual money transfer is outside the system.")
T("InstructorPayoutLines", G, ["InstructorPayoutId g >InstructorPayouts", "OrderId g >Orders", "OrderItemId g >OrderItems", "CourseId g >Courses", "GrossEarned m", "PaidEarned m"], flags="T", idx=[("InstructorPayoutId",), ("OrderId",)], note="Immutable record of what each payout earned per order line and course: GrossEarned before refunds, PaidEarned after refunds that existed at calculation. Refund debits are computed from these rows, never from recomputed activity.")
T("OrderRefunds", G, ["OrderId g >Orders", "Reference s:100", "Amount m", "Reason s:500?", "RecordedBy g? >AspNetUsers", "CreatedAtUtc d"], flags="T", uniq=[("OrderId", "Reference")], note="Accounting record of a refund decided and executed outside the system. It moves no money; it only reverses instructor earnings proportionally.")
T("InstructorDebits", G, ["InstructorId g >AspNetUsers", "OrderRefundId g >OrderRefunds", "SourcePayoutId g >InstructorPayouts", "Amount m", "RemainingAmount m", "Currency s:3", "CreatedAtUtc d"], flags="T", idx=[("InstructorId", "RemainingAmount")], note="Earnings already inside a calculated payout when their sale was refunded. Carried against the instructor's next payouts; never withdrawn automatically.")
T("InstructorDebitApplications", G, ["InstructorDebitId g >InstructorDebits", "InstructorPayoutId g >InstructorPayouts", "Amount m"], flags="T", idx=[("InstructorPayoutId",)], note="How much of a debit a payout absorbed. Cancelling the payout gives the amount back to the debit.")

# ---------------------------------------------------------------- Enterprise
G = "Enterprise & Analytics"
T("AuditLogs", G, [
    "UserId g? >AspNetUsers", "Action e:Create|Update|Delete|Restore|Approve|Reject|Generate|Login|ResetDevice|Other", "EntityName s:100", "EntityId s:64?",
    "OldValuesJson j?", "NewValuesJson j?", "IpAddress s:64?", "CorrelationId s:64?", "OccurredAtUtc d",
], flags="N", idx=[("TenantId", "OccurredAtUtc"), ("EntityName", "EntityId")],
  note="Append-only. Application DB user should have INSERT/SELECT only. Sensitive values (passwords, tokens) are never written.")
T("ApiKeys", G, ["Name s:200", "KeyPrefix s:12", "KeyHash s:128!", "ScopesJson j", "ExpiresAtUtc d?", "LastUsedAtUtc d?", "RevokedAtUtc d?"], flags="TA", note="Full key shown once at creation; only the hash is stored.")
T("WebhookEndpoints", G, ["Url s:1000", "SecretProtected t", "EventTypesJson j", "IsActive b =1", "ConsecutiveFailures i =0"], note="SecretProtected uses ASP.NET Data Protection. Deliveries are signed with HMAC-SHA256.")
T("WebhookDeliveries", G, [
    "WebhookEndpointId g >WebhookEndpoints", "EventType s:100", "PayloadJson j", "Status e:Pending|Delivered|Failed|Abandoned =Pending", "AttemptCount i =0", "NextAttemptAtUtc d?",
    "LastHttpStatus i?", "LastError s:1000?", "DeliveredAtUtc d?", "BackgroundJobId g? >BackgroundJobs",
], flags="TA", idx=[("Status", "NextAttemptAtUtc")])
T("LessonIndexStates", G, ["LessonId g >Lessons", "Status e:NotIndexed|Pending|Processing|Indexed|Failed =NotIndexed", "ContentHash s:64?", "ChunkCount i?", "IndexedAtUtc d?", "LastError s:1000?"], flags="TA", uniq=[("LessonId",)],
  note="Tracks what the AI service has embedded so ingest runs only when content changed.")
T("AiUsageLogs", G, ["UserId g? >AspNetUsers", "Endpoint s:80", "CourseId g? >Courses", "Provider s:30?", "LatencyMs i?", "StatusCode i", "CreatedAtUtc d"], flags="T", idx=[("UserId", "CreatedAtUtc")],
  note="No prompt text stored. Used for rate limiting and cost review.")
T("DropoutRiskScores", G, ["UserId g >AspNetUsers", "CourseId g >Courses", "Score f", "Level e:Low|Medium|High", "FeaturesJson j", "ComputedAtUtc d"], flags="T", idx=[("CourseId", "UserId", "ComputedAtUtc")])
T("AnalyticsSnapshots", G, ["Kind s:80", "CourseId g? >Courses", "PeriodStartUtc d", "PeriodEndUtc d", "DataJson j", "ComputedAtUtc d"], flags="T", idx=[("Kind", "PeriodStartUtc")],
  note="Pre-computed dashboards, drop-off heatmaps, leaderboards and revenue rollups written by Hangfire jobs.")
