using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Common;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Commerce;

/// <summary>Dashboards, report exports, the audit-log viewer and the soft-delete recycle bin. Everything is scoped to the current tenant by the query filters.</summary>
public sealed class AdminService(ApplicationDbContext db, ICurrentUser me, AuditWriter audit)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);

    // ---- dashboards ----------------------------------------------------------------------------------------------------
    public async Task<object> StudentDashboardAsync(CancellationToken ct)
    {
        var uid = Uid;
        var enr = await (from e in db.Enrollments.AsNoTracking() where e.UserId == uid && e.Status != EnrollmentStatus.Revoked join c in db.Courses on e.CourseId equals c.Id orderby e.EnrolledAtUtc descending select new { e.CourseId, c.Title, Status = e.Status.ToString(), e.ProgressPercent, e.ExpiresAtUtc, c.CategoryId }).Take(100).ToListAsync(ct);
        var courseIds = enr.Select(e => e.CourseId).ToList();
        var deadlines = await db.Assignments.AsNoTracking().Where(a => courseIds.Contains(a.CourseId) && a.IsPublished && a.DueAtUtc > Now).OrderBy(a => a.DueAtUtc).Take(10).Select(a => new { a.Id, a.CourseId, a.Title, a.DueAtUtc }).ToListAsync(ct);
        var xp = await db.XpTransactions.Where(x => x.UserId == uid).SumAsync(x => (int?)x.Points, ct) ?? 0;
        var streak = await db.UserStreaks.AsNoTracking().Where(s => s.UserId == uid).Select(s => new { CurrentStreak = s.CurrentStreakDays, LongestStreak = s.LongestStreakDays }).FirstOrDefaultAsync(ct);
        // Skill radar: average finished exam score (percent) per course category.
        var scores = await (from a in db.ExamAttempts.AsNoTracking() where a.UserId == uid && a.Score != null && a.MaxScore > 0
                            join ex in db.Exams on a.ExamId equals ex.Id join c in db.Courses on ex.CourseId equals c.Id
                            select new { c.CategoryId, Pct = a.Score!.Value / a.MaxScore!.Value * 100 }).ToListAsync(ct);
        var cats = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var radar = scores.GroupBy(s => s.CategoryId).Select(g => new { skill = g.Key is { } k && cats.TryGetValue(k, out var n) ? n : "General", averagePercent = Math.Round(g.Average(x => x.Pct), 1), attempts = g.Count() }).ToList();
        return new { enrollments = enr.Select(e => new { e.CourseId, e.Title, e.Status, e.ProgressPercent, e.ExpiresAtUtc }), upcomingDeadlines = deadlines, xp, streak = streak?.CurrentStreak ?? 0, longestStreak = streak?.LongestStreak ?? 0, skillRadar = radar };
    }

    public async Task<object> InstructorDashboardAsync(CancellationToken ct)
    {
        var uid = Uid;
        var courseIds = await db.Courses.AsNoTracking().Where(c => c.OwnerInstructorId == uid).Select(c => c.Id).ToListAsync(ct);
        var since = Now.AddDays(-84);
        var enrol = await db.Enrollments.AsNoTracking().Where(e => courseIds.Contains(e.CourseId) && e.EnrolledAtUtc >= since).Select(e => e.EnrolledAtUtc).ToListAsync(ct);
        var trend = enrol.GroupBy(d => DateOnly.FromDateTime(d.AddDays(-(((int)d.DayOfWeek + 6) % 7)))).OrderBy(g => g.Key).Select(g => new { weekStart = g.Key, enrollments = g.Count() }).ToList();
        // Drop-off: for each lesson, how many students opened it and how many finished it.
        var prog = await db.LessonProgress.AsNoTracking().Where(p => courseIds.Contains(p.CourseId)).GroupBy(p => p.LessonId).Select(g => new { LessonId = g.Key, started = g.Count(), completed = g.Count(p => p.Status == LessonProgressStatus.Completed) }).ToListAsync(ct);
        var lessons = await db.Lessons.AsNoTracking().Where(l => courseIds.Contains(l.CourseId)).Select(l => new { l.Id, l.CourseId, l.Title, l.SortOrder }).ToListAsync(ct);
        var heat = lessons.Select(l => { var p = prog.FirstOrDefault(x => x.LessonId == l.Id); var s = p?.started ?? 0; var c = p?.completed ?? 0; return new { lessonId = l.Id, l.CourseId, l.Title, started = s, completed = c, dropOffPercent = s == 0 ? 0 : Math.Round((1 - (double)c / s) * 100, 1) }; }).OrderBy(x => x.CourseId).ToList();
        // Quiz difficulty: share of graded answers that were correct, per question (hardest first).
        var diff = await (from a in db.ExamAnswers.AsNoTracking() where a.IsCorrect != null
                          join aq in db.ExamAttemptQuestions on a.ExamAttemptQuestionId equals aq.Id
                          join q in db.Questions on aq.QuestionId equals q.Id
                          join ex in db.ExamAttempts on aq.ExamAttemptId equals ex.Id
                          join e in db.Exams on ex.ExamId equals e.Id where courseIds.Contains(e.CourseId)
                          group a by new { q.Id, q.Text } into g select new { questionId = g.Key.Id, text = g.Key.Text, answers = g.Count(), correctPercent = Math.Round(g.Count(x => x.IsCorrect == true) * 100.0 / g.Count(), 1) })
                          .OrderBy(x => x.correctPercent).Take(20).ToListAsync(ct);
        var rev = await db.OrderItems.AsNoTracking().Where(i => i.CourseId != null && courseIds.Contains(i.CourseId.Value) && db.Orders.Any(o => o.Id == i.OrderId && o.Status == OrderStatus.Paid)).SumAsync(i => (decimal?)i.LineTotal, ct) ?? 0;
        return new { courses = courseIds.Count, totalStudents = await db.Enrollments.Where(e => courseIds.Contains(e.CourseId)).Select(e => e.UserId).Distinct().CountAsync(ct), enrollmentTrend = trend, lessonDropOff = heat, quizDifficulty = diff, grossRevenue = rev, revenueNote = "Gross sales of your courses before discounts and your share. Use GET /instructor/revenue for earnings." };
    }

    public async Task<object> AdminDashboardAsync(CancellationToken ct)
    {
        var since = Now.AddDays(-30);
        var paid = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Paid);
        var kpis = new
        {
            users = await db.Users.CountAsync(u => u.TenantId == Tid && u.IsActive, ct),
            publishedCourses = await db.Courses.CountAsync(c => c.Status == CourseStatus.Published, ct),
            activeEnrollments = await db.Enrollments.CountAsync(e => e.Status == EnrollmentStatus.Active, ct),
            revenueLast30Days = await paid.Where(o => o.PaidAtUtc >= since).SumAsync(o => (decimal?)o.Total, ct) ?? 0,
            revenueAllTime = await paid.SumAsync(o => (decimal?)o.Total, ct) ?? 0,
            paidOrdersLast30Days = await paid.CountAsync(o => o.PaidAtUtc >= since, ct),
        };
        var queues = new
        {
            coursesAwaitingApproval = await db.Courses.CountAsync(c => c.Status == CourseStatus.PendingApproval, ct),
            manualPaymentsToReview = await db.ManualPaymentSubmissions.CountAsync(m => m.Status == ManualPaymentSubmissionStatus.Pending, ct),
            payoutsToApprove = await db.InstructorPayouts.CountAsync(p => p.Status == InstructorPayoutStatus.Calculated, ct),
            failedWebhookDeliveries = await db.WebhookDeliveries.CountAsync(d => d.Status == WebhookDeliveryStatus.Failed || d.Status == WebhookDeliveryStatus.Abandoned, ct),
        };
        var daily = (await paid.Where(o => o.PaidAtUtc >= since).Select(o => new { o.PaidAtUtc, o.Total }).ToListAsync(ct)).GroupBy(o => DateOnly.FromDateTime(o.PaidAtUtc!.Value)).OrderBy(g => g.Key).Select(g => new { day = g.Key, revenue = g.Sum(x => x.Total), orders = g.Count() }).ToList();
        return new { kpis, queues, revenueByDay = daily };
    }

    // ---- reports -------------------------------------------------------------------------------------------------------
    public const int MaxReportRows = 20000;
    public async Task<(byte[] Bytes, string Type, string Name)> ExportAsync(string kind, string? format, DateTime? from, DateTime? to, CancellationToken ct)
    {
        kind = kind.ToLowerInvariant(); var xlsx = string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);
        if (!xlsx && !string.IsNullOrWhiteSpace(format) && !string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)) throw new ValidationAppException("Format must be csv or xlsx.");
        var f = from ?? Now.AddMonths(-1); var t = to ?? Now.AddSeconds(1);
        if (t <= f || t - f > TimeSpan.FromDays(366)) throw new ValidationAppException("The range must be positive and at most one year.");
        var instructor = !IsAdmin;
        if (instructor && kind is not ("enrollments" or "revenue")) throw new NotFoundException("Report not found.");
        List<string> head; List<List<object?>> rows;
        switch (kind)
        {
            case "orders":
                head = ["order_id", "user_id", "status", "payment_method", "subtotal", "discount", "total", "currency", "created_utc", "paid_utc"];
                rows = (await db.Orders.AsNoTracking().Where(o => o.CreatedAtUtc >= f && o.CreatedAtUtc < t).OrderBy(o => o.CreatedAtUtc).Take(MaxReportRows + 1).ToListAsync(ct)).Select(o => new List<object?> { o.Id, o.UserId, o.Status, o.PaymentMethod, o.Subtotal, o.DiscountTotal, o.Total, o.Currency, o.CreatedAtUtc, o.PaidAtUtc }).ToList(); break;
            case "enrollments":
                head = ["enrollment_id", "user_id", "course_id", "course_title", "source", "status", "progress_percent", "enrolled_utc"];
                var q = from e in db.Enrollments.AsNoTracking() join c in db.Courses on e.CourseId equals c.Id where e.EnrolledAtUtc >= f && e.EnrolledAtUtc < t && (!instructor || c.OwnerInstructorId == Uid) orderby e.EnrolledAtUtc select new { e.Id, e.UserId, e.CourseId, c.Title, e.Source, e.Status, e.ProgressPercent, e.EnrolledAtUtc };
                rows = (await q.Take(MaxReportRows + 1).ToListAsync(ct)).Select(e => new List<object?> { e.Id, e.UserId, e.CourseId, e.Title, e.Source, e.Status, e.ProgressPercent, e.EnrolledAtUtc }).ToList(); break;
            case "revenue":
                head = ["order_id", "paid_utc", "course_id", "item", "line_total", "currency"];
                var rq = from i in db.OrderItems.AsNoTracking() join o in db.Orders on i.OrderId equals o.Id
                         join c in db.Courses on i.CourseId equals c.Id into cj from c in cj.DefaultIfEmpty()
                         where o.Status == OrderStatus.Paid && o.PaidAtUtc >= f && o.PaidAtUtc < t && (!instructor || (c != null && c.OwnerInstructorId == Uid)) orderby o.PaidAtUtc select new { o.Id, o.PaidAtUtc, i.CourseId, i.Description, i.LineTotal, o.Currency };
                rows = (await rq.Take(MaxReportRows + 1).ToListAsync(ct)).Select(r => new List<object?> { r.Id, r.PaidAtUtc, r.CourseId, r.Description, r.LineTotal, r.Currency }).ToList(); break;
            case "payouts":
                head = ["payout_id", "instructor_id", "period_start_utc", "period_end_utc", "amount", "currency", "status", "payment_reference"];
                rows = (await db.InstructorPayouts.AsNoTracking().Where(p => p.CreatedAtUtc >= f && p.CreatedAtUtc < t).OrderBy(p => p.CreatedAtUtc).Take(MaxReportRows + 1).ToListAsync(ct)).Select(p => new List<object?> { p.Id, p.InstructorId, p.PeriodStartUtc, p.PeriodEndUtc, p.Amount, p.Currency, p.Status, p.PaymentReference }).ToList(); break;
            default: throw new NotFoundException("Unknown report. Use orders, enrollments, revenue or payouts.");
        }
        if (rows.Count > MaxReportRows) throw new AppException(413, "REPORT_TOO_LARGE", $"This report has more than {MaxReportRows} rows. Narrow the date range.");
        await audit.WriteAsync(AuditLogAction.Other, "ReportExport", kind, new { format = xlsx ? "xlsx" : "csv", rows = rows.Count }, ct);
        var name = $"{kind}-{f:yyyyMMdd}-{t:yyyyMMdd}";
        return xlsx ? (Xlsx(head, rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx") : (Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(head, rows))).ToArray(), "text/csv; charset=utf-8", name + ".csv");
    }

    private static string Cell(object? v)
    {
        var s = v switch { null => "", DateTime d => d.ToString("O"), decimal m => m.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), double x => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
        return s;
    }
    /// <summary>Cells that start with = + - @ are prefixed with an apostrophe so a spreadsheet never runs them as a formula.</summary>
    private static string Safe(string s) => s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _) ? "'" + s : s;
    private static string Csv(List<string> head, List<List<object?>> rows)
    {
        static string Q(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder(string.Join(',', head)).Append("\r\n");
        foreach (var r in rows) sb.Append(string.Join(',', r.Select(c => Q(Safe(Cell(c)))))).Append("\r\n");
        return sb.ToString();
    }
    private static byte[] Xlsx(List<string> head, List<List<object?>> rows)
    {
        static string X(string s) => System.Security.SecurityElement.Escape(new string(s.Where(c => c >= ' ' || c is '\t' or '\n' or '\r').ToArray())) ?? "";
        static string Col(int i) => ((char)('A' + i)).ToString();
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols>");
        for (var ci = 0; ci < head.Count; ci++)
        {
            var w = Math.Max(head[ci].Length, rows.Count == 0 ? 0 : rows.Max(r => ci < r.Count ? Cell(r[ci]).Length : 0));
            sheet.Append($"<col min=\"{ci + 1}\" max=\"{ci + 1}\" width=\"{Math.Min(60, w + 3)}\" customWidth=\"1\"/>");
        }
        sheet.Append("</cols><sheetData>");
        void Row(int n, IEnumerable<object?> cells)
        {
            sheet.Append($"<row r=\"{n}\">"); var i = 0; var st = n == 1 ? " s=\"1\"" : "";
            foreach (var c in cells)
            {
                var r = Col(i++) + n;
                if (c is decimal or double or int or long) sheet.Append($"<c r=\"{r}\"{st}><v>{Convert.ToString(c, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                else sheet.Append($"<c r=\"{r}\"{st} t=\"inlineStr\"><is><t>{X(Safe(Cell(c)))}</t></is></c>");
            }
            sheet.Append("</row>");
        }
        Row(1, head.Cast<object?>()); for (var i = 0; i < rows.Count; i++) Row(i + 2, rows[i]);
        sheet.Append("</sheetData></worksheet>");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string path, string content) { var e = zip.CreateEntry(path); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(content); }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F3A6E\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/></cellXfs></styleSheet>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    // ---- audit logs ----------------------------------------------------------------------------------------------------
    public async Task<object> AuditLogsAsync(Guid? userId, string? entity, string? action, DateTime? from, DateTime? to, PagedQueryLite q, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (userId is { } u) query = query.Where(a => a.UserId == u);
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(a => a.EntityName == entity);
        if (!string.IsNullOrWhiteSpace(action)) { var act = Enum.TryParse<AuditLogAction>(action, true, out var x) ? x : throw new ValidationAppException("Unknown action."); query = query.Where(a => a.Action == act); }
        if (from is { } f) query = query.Where(a => a.OccurredAtUtc >= f);
        if (to is { } t) query = query.Where(a => a.OccurredAtUtc < t);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.OccurredAtUtc).Skip(q.Skip).Take(q.Size).Select(a => new { a.Id, a.UserId, userName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault(), Action = a.Action.ToString(), a.EntityName, a.EntityId, a.OldValuesJson, a.NewValuesJson, a.IpAddress, a.CorrelationId, a.OccurredAtUtc }).ToListAsync(ct);
        return new { items, page = q.Page, pageSize = q.Size, totalCount = total };
    }

    // ---- recycle bin ---------------------------------------------------------------------------------------------------
    private sealed record Bin(string Name, Func<ApplicationDbContext, Guid, Task<List<BinRow>>> List, Func<ApplicationDbContext, Guid, Guid, Task<bool>> Exists, Func<ApplicationDbContext, Guid, Guid, Task<int>> Restore, Func<ApplicationDbContext, Guid, Guid, Task<bool>>? ParentAlive = null);
    public sealed record BinRow(string Entity, Guid Id, string Name, DateTime? DeletedAtUtc, Guid? DeletedBy);

    private static Bin Make<T>(string name, Func<T, string> label) where T : BaseEntity, ISoftDeletable, ITenantEntity =>
        new(name,
            async (db, tid) => (await db.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tid && x.IsDeleted).OrderByDescending(x => x.DeletedAtUtc).Take(200).ToListAsync()).Select(x => new BinRow(name, x.Id, label(x), x.DeletedAtUtc, x.DeletedBy)).ToList(),
            async (db, tid, id) => await db.Set<T>().IgnoreQueryFilters().AnyAsync(x => x.Id == id && x.TenantId == tid && x.IsDeleted),
            async (db, tid, id) => await db.Set<T>().IgnoreQueryFilters().Where(x => x.Id == id && x.TenantId == tid && x.IsDeleted).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, false).SetProperty(x => x.DeletedAtUtc, (DateTime?)null).SetProperty(x => x.DeletedBy, (Guid?)null)));

    private static readonly Dictionary<string, Bin> Bins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["course"] = Make<Course>("course", x => x.Title),
        ["section"] = Make<Section>("section", x => x.Title) with { ParentAlive = async (db, tid, id) => (await db.Sections.IgnoreQueryFilters().Where(s => s.Id == id).Select(s => s.CourseId).FirstOrDefaultAsync()) is var c && await db.Courses.AnyAsync(x => x.Id == c) },
        ["lesson"] = Make<Lesson>("lesson", x => x.Title) with { ParentAlive = async (db, tid, id) => (await db.Lessons.IgnoreQueryFilters().Where(l => l.Id == id).Select(l => new { l.CourseId, l.SectionId }).FirstOrDefaultAsync()) is { } p && await db.Courses.AnyAsync(x => x.Id == p.CourseId) && await db.Sections.AnyAsync(x => x.Id == p.SectionId) },
        ["learning-path"] = Make<LearningPath>("learning-path", x => x.Title),
        ["assignment"] = Make<Assignment>("assignment", x => x.Title) with { ParentAlive = async (db, tid, id) => (await db.Assignments.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.CourseId).FirstOrDefaultAsync()) is var c && await db.Courses.AnyAsync(x => x.Id == c) },
        ["coupon"] = Make<Coupon>("coupon", x => x.Code),
        ["bundle"] = Make<Bundle>("bundle", x => x.Name),
        ["subscription-plan"] = Make<SubscriptionPlan>("subscription-plan", x => x.Name),
    };

    public async Task<object> RecycleBinAsync(string? entity, CancellationToken ct)
    {
        var tid = Tid; var bins = string.IsNullOrWhiteSpace(entity) ? Bins.Values.ToList() : [Bins.GetValueOrDefault(entity.Trim()) ?? throw new ValidationAppException("Unknown entity. Use: " + string.Join(", ", Bins.Keys))];
        var all = new List<BinRow>(); foreach (var b in bins) all.AddRange(await b.List(db, tid));
        return new { items = all.OrderByDescending(r => r.DeletedAtUtc), supportedEntities = Bins.Keys };
    }

    public async Task RestoreAsync(string entity, Guid id, CancellationToken ct)
    {
        var bin = Bins.GetValueOrDefault(entity.Trim()) ?? throw new ValidationAppException("Unknown entity. Use: " + string.Join(", ", Bins.Keys));
        var tid = Tid;
        if (!await bin.Exists(db, tid, id)) throw new NotFoundException("Nothing to restore.");
        if (bin.ParentAlive is { } alive && !await alive(db, tid, id)) throw new ConflictAppException("PARENT_DELETED", "Restore the parent (course or section) first.");
        int n;
        try { n = await bin.Restore(db, tid, id); }
        catch (DbUpdateException) { throw new ConflictAppException("RESTORE_CONFLICT", "An active item with the same unique name or code exists. Rename or remove it first."); }
        if (n == 0) throw new NotFoundException("Nothing to restore.");
        await audit.WriteAsync(AuditLogAction.Restore, bin.Name, id, null, ct);
    }
}
