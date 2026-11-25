SmartLMS backend update - round 2 (additive, review-only)
=========================================================

What this is
------------
Twelve changed source files that close workflow gaps the frontend depends
on. Nothing else in the backend is touched. The same delta is included as
a unified diff (smartlms-backend-round2-additions.patch) for review.

Changes
-------
1. SmartLMS.Infrastructure/Services/Lms/AssignmentService.cs
   The assignment list response gains a per-assignment "mine" field: the
   caller's newest submission summary (their own, or their team's) with
   id, version, status, totalScore, isLate and submittedAtUtc. It is null
   when the caller never submitted. Students use it to reopen their work
   and see their grade after leaving the page.
2. SmartLMS.Infrastructure/Services/Lms/CommunityService.cs
   - ManagePathsAsync: staff list of learning paths including drafts.
     TenantAdmin sees all tenant paths; instructors only paths they created
     (same ownership rule as UpdatePathAsync).
   - ReplacePathCoursesAsync now preserves IsMandatory for re-linked
     courses (before, every edit silently reset mandatory flags).
3. SmartLMS.API/Controllers/CommunityController.cs
   New route GET /learning-paths/manage (TenantAdmin, Instructor).
4. SmartLMS.Infrastructure/Services/Lms/EngagementServices.cs
   PendingLinksAsync: pending parent-link requests with parent and student
   display names, tenant-filtered (discovery for the approvals screen).
5. SmartLMS.API/Controllers/EngagementController.cs
   New route GET /parent/links/pending (TenantAdmin, Instructor).
6. SmartLMS.Infrastructure/Services/Commerce/CommerceService.cs
   MySubscriptionsAsync: the caller's subscriptions joined with plan
   display fields (id, planName, price, currency, billingPeriod, status,
   startedAtUtc, currentPeriodEndUtc, cancelledAtUtc). Powers the "My
   subscriptions" section and its cancel confirmation.
7. SmartLMS.API/Controllers/CommerceController.cs
   New route GET /subscriptions/mine (Student).
8. SmartLMS.Infrastructure/Services/Lms/ContentService.cs + SmartLMS.API/Controllers/ContentController.cs
   New route GET /courses/{id}/prerequisites (TenantAdmin, Instructor):
   the current prerequisite list with titles, so the builder prefills
   instead of overwriting blind.
9. SmartLMS.Infrastructure/Services/Lms/AssessmentService.cs + SmartLMS.API/Controllers/AssessmentsController.cs
   New route GET /exams/{id} (TenantAdmin, Instructor): full editable
   exam settings (instructions, availability window, tab-switch options)
   plus a locked flag when a published exam has attempts, so the exam
   editor prefills instead of overwriting blind. UpdateExamAsync remains
   settings-only and never touches question rules.
10. SmartLMS.Infrastructure/Services/Commerce/RevenueService.cs + the
   learning-weight route in SmartLMS.API/Controllers/CommerceController.cs
   New route GET /courses/{courseId}/learning-weight (TenantAdmin):
   the current non-video learning weight (null when never set), so the
   finance form shows the current value instead of overwriting blind.

All data access stays inside the existing tenant global filter and the
existing role checks. No schema or migration changes.

IMPORTANT - test status
-----------------------
SUPERSEDED 2026-11-25: this delta has since been compiled and tested - see README-VERIFICATION.txt (dotnet build 0 errors, 143/143 tests pass).
Originally, these files were NOT compiled or run here (no .NET SDK available in the
build environment). Treat them as review-ready source: compile and test
them on your machine before deploying. The frontend already works with an
unpatched backend: where the new GET endpoints are missing (404) the UI
shows an "update required" notice and falls back gracefully instead of
failing; 401/403/5xx surface as real errors.

Install
-------
1. Back up the twelve originals from your backend source tree (same paths
   as under files/):
     copy each src/<path> to src/<path>.bak-round2
   or commit your current tree so you can revert with git.
2. Copy the twelve files from files/ over src/ (paths already match).
3. Build:  dotnet build backend/src/SmartLMS.API
4. Run your normal test pass, then deploy as usual.
5. Rollback: restore the .bak-round2 files (or git revert) and rebuild.

After deploy, verify in the UI:
- Staff see drafts under Learning paths (no "update required" notice).
- Approvals shows the pending parent-link requests section.
- A student re-opening an assignment still sees their newest submission
  and its grade.
- Orders shows the "My subscriptions" section for a subscribed student.
- Assessments: Edit on an exam opens prefilled settings (title,
  instructions, availability window) instead of blanks.
- Builder: the prerequisites section lists current prerequisites instead
  of the update-required notice.
- Finance > Controls: picking a course under Course learning weight
  shows its current value instead of the update-required notice.
