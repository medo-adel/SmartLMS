using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Controllers;

[Route("api/v1")]
public sealed class EngagementController(ParentService parents, CertificateService certs, GamificationService xp, JobService jobs) : ApiControllerBase
{
    private const string Mgr = $"{RoleNames.TenantAdmin},{RoleNames.Instructor}";

    [HttpPost("students/me/link-codes"), Authorize(Roles = RoleNames.Student)]
    public ActionResult<ApiResponse<object>> LinkCode() => Success(parents.CreateCode());
    [HttpPost("parent/links"), Authorize(Roles = RoleNames.Parent)]
    public async Task<ActionResult<ApiResponse<object>>> RequestLink(LinkRequest r, CancellationToken ct) => Success(await parents.RequestLinkAsync(r, ct));
    [HttpGet("parent/links/pending"), Authorize(Roles = Mgr)]
    public async Task<ActionResult<ApiResponse<object>>> PendingLinks(CancellationToken ct) => Success(await parents.PendingLinksAsync(ct));
    [HttpPut("parent/links/{id:guid}"), Authorize(Roles = Mgr)]
    public async Task<ActionResult<ApiResponse<object?>>> SetLink(Guid id, ApproveLinkRequest r, CancellationToken ct) { await parents.SetLinkAsync(id, r.Approve, ct); return Done(r.Approve ? "Approved." : "Revoked."); }
    [HttpGet("parent/children"), Authorize(Roles = RoleNames.Parent)]
    public async Task<ActionResult<ApiResponse<object>>> Children(CancellationToken ct) => Success(await parents.ChildrenAsync(ct));
    [HttpGet("parent/children/{studentId:guid}/summary"), Authorize(Roles = RoleNames.Parent)]
    public async Task<ActionResult<ApiResponse<object>>> Summary(Guid studentId, CancellationToken ct) => Success(await parents.SummaryAsync(studentId, ct));
    [HttpGet("parent/alerts"), Authorize(Roles = RoleNames.Parent)]
    public async Task<ActionResult<ApiResponse<object>>> Alerts(CancellationToken ct) => Success(await parents.AlertsAsync(ct));

    [HttpPost("certificates/issue"), Authorize(Roles = Mgr)]
    public async Task<ActionResult<ApiResponse<object>>> Issue(IssueCertificateRequest r, CancellationToken ct) => Success(await certs.IssueManualAsync(r, ct));
    [HttpGet("certificates/mine"), Authorize(Roles = RoleNames.Student)]
    public async Task<ActionResult<ApiResponse<object>>> Mine(CancellationToken ct) => Success(await certs.MineAsync(ct));
    [HttpGet("certificates/verify/{code}")]
    public async Task<ActionResult<ApiResponse<object>>> Verify(string code, CancellationToken ct) => Success(await certs.VerifyAsync(code, ct));
    [HttpGet("certificates/{id:guid}/pdf"), Authorize(Roles = $"{RoleNames.Student},{Mgr}")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct) =>
        File(await certs.PdfAsync(id, $"{Request.Scheme}://{Request.Host}", ct), "application/pdf", $"certificate-{id:N}.pdf");

    [HttpGet("gamification/me"), Authorize(Roles = RoleNames.Student)]
    public async Task<ActionResult<ApiResponse<object>>> Me(CancellationToken ct) => Success(await xp.MeAsync(ct));
    [HttpGet("gamification/leaderboard"), Authorize(Roles = $"{RoleNames.Student},{Mgr}")]
    public async Task<ActionResult<ApiResponse<object>>> Board([FromQuery] Guid? courseId, CancellationToken ct) => Success(await xp.LeaderboardAsync(courseId, ct));

    [HttpGet("jobs/{id:guid}"), Authorize]
    public async Task<ActionResult<ApiResponse<object>>> Job(Guid id, CancellationToken ct) => Success(await jobs.GetAsync(id, ct));
}
