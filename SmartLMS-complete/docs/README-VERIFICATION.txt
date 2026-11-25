SmartLMS backend - verification supplement (OPTIONAL)
=====================================================

This supplement is separate from the 12 production files in
smartlms-backend-update-round2.zip. It changes no production code.
It adds one clearly labeled test file plus this verification record.

WHAT WAS VERIFIED (local, isolated environment)
-----------------------------------------------
Source tested: the pristine uploaded backend, byte-identical to the
uploaded zip, plus the delivered 12-file update applied (12/12 files).
No other modifications.

SDK:      .NET SDK 10.0.404 (installed in isolation via dotnet-install.sh)
Build:    dotnet build SmartLMS.sln
          -> SUCCEEDED, 0 errors, 7 warnings (all 7 pre-existing in the
             uploaded original; the update adds no new warnings or errors)
Tests:    dotnet test SmartLMS.sln
          -> 139/139 existing tests PASSED, 0 failed (~1m38s)
          -> +4 new tests in Round2AdditionsTests.cs PASSED
          -> 143/143 total PASSED

WHAT THE ADDED TEST COVERS
--------------------------
The existing suite exercised only the pre-existing PUT routes.
Round2AdditionsTests.cs adds smoke coverage for all 6 GET routes the
update introduces, each checked for the same ownership/role guard as
its corresponding write:

  GET /exams/{id}                         settings-only payload, no answer
                                          leak; 404 for non-owner, 403 student
  GET /courses/{id}/prerequisites         returns the list previously set;
                                          404 for a course owned by another
                                          instructor
  GET /courses/{courseId}/learning-weight null default, then 1800 after PUT;
                                          admin-only (403 for instructor)
  GET /subscriptions/mine                 returns list; 403 for instructor
  GET /learning-paths/manage              instructor sees only own-tenant rows
  GET /parent/links/pending               populated pending row via the real
                                          link-code flow

HOW TO APPLY (optional)
-----------------------
1. Copy tests/SmartLMS.Tests/Round2AdditionsTests.cs from this supplement
   into your backend's tests/SmartLMS.Tests/ folder.
2. Run the full suite:
       dotnet test SmartLMS.sln
   or just the new tests:
       dotnet test SmartLMS.sln --filter "FullyQualifiedName~Round2AdditionsTests"
   Expected: 4 passed for the filter; 143 passed for the full suite.

HONEST LIMITS
-------------
The test host runs the real API against a local SQLite file database,
which approximates but is not identical to SQL Server. These results
prove the delivered code compiles cleanly, routes correctly, and enforces
ownership/role guards in the integration test host. They do not replace
verifying login, course publish, enrollment, and student flows against
your live environment.

NOTE (2026-11-25): this record SUPERSEDES the "not compiled or run"
notes in README-INSTALL.txt and COVERAGE-NOTES.md from the earlier
round-2 delivery. The exact code bundled here builds clean and passes
143/143 tests. The one added test file has since had a style warning
fixed (Assert.True(x.StartsWith) -> Assert.StartsWith); the copy in
this bundle's backend/tests folder is the warning-free version.
