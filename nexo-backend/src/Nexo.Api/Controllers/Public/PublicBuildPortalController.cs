using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Public;

/// <summary>
/// Public client portal of Orken Build (no login) — contract v1.1, docs/build/client-portal-contract.md.
/// Address: <c>/api/public/build/{companySlug}/{projectSlug}</c>. Unknown and unpublished portals answer
/// the same 404. A password portal needs the <c>X-Build-Portal-Session</c> header (token from POST …/session).
/// Responses are never cached (they may carry signed media URLs).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/build/{companySlug}/{projectSlug}")]
[BuildPortalErrors]
public class PublicBuildPortalController : ControllerBase
{
    private readonly BuildPublicPortalService _portal;

    public PublicBuildPortalController(BuildPublicPortalService portal) => _portal = portal;

    private string? SessionToken => Request.Headers[PublicBuildPortal.SessionHeader].FirstOrDefault();

    /// <summary>Everything the portal home needs, limited to the sections the company enabled.</summary>
    [HttpGet]
    [EnableRateLimiting("build-portal")]
    public async Task<ActionResult<PublicBuildBootstrapDto>> Bootstrap(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetBootstrapAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>Opens a session of a password portal: { token, expiresAt }. Send the token in X-Build-Portal-Session.</summary>
    [HttpPost("session")]
    [EnableRateLimiting("build-portal-auth")]
    public async Task<ActionResult<PublicBuildSessionDto>> OpenSession(
        string companySlug, string projectSlug, [FromBody] CreatePublicBuildSessionRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.OpenSessionAsync(companySlug, projectSlug, request?.Password, ct));
    }

    /// <summary>Ends the session in X-Build-Portal-Session (idempotent).</summary>
    [HttpDelete("session")]
    [EnableRateLimiting("build-portal")]
    public async Task<IActionResult> CloseSession(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        await _portal.CloseSessionAsync(companySlug, projectSlug, SessionToken, ct);
        return NoContent();
    }

    /// <summary>Newest first: published updates, client-visible daily log summaries, stage progress, photos.</summary>
    [HttpGet("timeline")]
    [EnableRateLimiting("build-portal")]
    public async Task<ActionResult<PublicBuildTimelineDto>> Timeline(
        string companySlug, string projectSlug, [FromQuery] DateTimeOffset? before, [FromQuery] int? take, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetTimelineAsync(companySlug, projectSlug, SessionToken, before, take, ct));
    }

    /// <summary>Client-visible site photos (signed URLs valid ~15 minutes).</summary>
    [HttpGet("photos")]
    [EnableRateLimiting("build-portal")]
    public async Task<ActionResult<PublicBuildPhotosDto>> Photos(
        string companySlug, string projectSlug, [FromQuery] int? skip, [FromQuery] int? take, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetPhotosAsync(companySlug, projectSlug, SessionToken, skip, take, ct));
    }

    /// <summary>Progress curves (actual from daily snapshots, planned, forecast when enabled).</summary>
    [HttpGet("progress")]
    [EnableRateLimiting("build-portal")]
    public async Task<ActionResult<PublicBuildProgressDto>> Progress(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetProgressAsync(companySlug, projectSlug, SessionToken, ct));
    }

    // ── v1.1: change orders, selections, measurements ─────────────────────────

    /// <summary>Change orders sent to the client (pending) and decided ones (flag changeOrders).</summary>
    [HttpGet("change-orders")]
    [EnableRateLimiting("build-portal")]
    public async Task<ActionResult<PublicBuildChangeOrdersDto>> ChangeOrders(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetChangeOrdersAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>The client approves/rejects a pending change order — password portal with a session only.</summary>
    [HttpPost("change-orders/{publicId:guid}/decision")]
    [EnableRateLimiting("build-portal-decision")]
    public async Task<ActionResult<PublicBuildChangeOrderDto>> DecideChangeOrder(string companySlug, string projectSlug, Guid publicId,
        [FromBody] PublicBuildChangeOrderDecisionRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.DecideChangeOrderAsync(companySlug, projectSlug, SessionToken, publicId, request, ct));
    }

    /// <summary>The client's selections and their options (flag selections).</summary>
    [HttpGet("selections")]
    [EnableRateLimiting("build-portal")]
    public async Task<ActionResult<PublicBuildSelectionsDto>> Selections(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetSelectionsAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>The client chooses an option — password portal with a session, selection open, before the deadline.</summary>
    [HttpPost("selections/{publicId:guid}/choice")]
    [EnableRateLimiting("build-portal-decision")]
    public async Task<ActionResult<PublicBuildSelectionDto>> ChooseSelection(string companySlug, string projectSlug, Guid publicId,
        [FromBody] PublicBuildSelectionChoiceRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.ChooseSelectionAsync(companySlug, projectSlug, SessionToken, publicId, request, ct));
    }

    /// <summary>Approved measurements (flag measurements; values only with financialSummary).</summary>
    [HttpGet("measurements")]
    [EnableRateLimiting("build-portal")]
    public async Task<ActionResult<PublicBuildMeasurementsDto>> Measurements(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetMeasurementsAsync(companySlug, projectSlug, SessionToken, ct));
    }

    private void NoStore() => Response.Headers.CacheControl = "no-store";
}

/// <summary>
/// Maps <see cref="BuildPortalAccessException"/> to its status with a fixed body (no request id), so an
/// unknown and an unpublished portal answer byte-identical 404s.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BuildPortalErrorsAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not BuildPortalAccessException ex) return;
        object body = ex.Portal is not null
            ? new { error = ex.Message, code = ex.Code, portal = ex.Portal }
            : ex.RetryAfterSeconds is { } retry
                ? new { error = ex.Message, code = ex.Code, retryAfterSeconds = retry }
                : new { error = ex.Message, code = ex.Code };
        if (ex.RetryAfterSeconds is { } seconds)
            context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, seconds).ToString();
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(body) { StatusCode = ex.Status };
        context.ExceptionHandled = true;
    }
}
