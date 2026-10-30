using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Public;

/// <summary>
/// Public client portal of Orken Build (no login) — contract v1.4, docs/build/client-portal-contract.md. Every action declares
/// its success type and the shared error body (<see cref="PublicBuildErrorDto"/>) for the OpenAPI document exported to
/// docs/build/openapi-portal.json.
/// Address: <c>/api/public/build/{companySlug}/{projectSlug}</c>. Unknown and unpublished portals answer
/// the same 404. A password portal needs the <c>X-Build-Portal-Session</c> header (token from POST …/session).
/// Responses are never cached (they may carry signed media URLs).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/build/{companySlug}/{projectSlug}")]
[BuildPortalErrors]
[Produces("application/json")]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status422UnprocessableEntity)]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status500InternalServerError)]
public class PublicBuildPortalController : ControllerBase
{
    private readonly BuildPublicPortalService _portal;

    public PublicBuildPortalController(BuildPublicPortalService portal) => _portal = portal;

    private string? SessionToken => Request.Headers[PublicBuildPortal.SessionHeader].FirstOrDefault();

    /// <summary>Everything the portal home needs, limited to the sections the company enabled.</summary>
    [HttpGet]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildBootstrapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildBootstrapDto>> Bootstrap(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetBootstrapAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>Opens a session of a password portal: { token, expiresAt }. Send the token in X-Build-Portal-Session.</summary>
    [HttpPost("session")]
    [EnableRateLimiting("build-portal-auth")]
    [ProducesResponseType(typeof(PublicBuildSessionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildSessionDto>> OpenSession(
        string companySlug, string projectSlug, [FromBody] CreatePublicBuildSessionRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.OpenSessionAsync(companySlug, projectSlug, request?.Password, ct));
    }

    /// <summary>Ends the session in X-Build-Portal-Session (idempotent).</summary>
    [HttpDelete("session")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CloseSession(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        await _portal.CloseSessionAsync(companySlug, projectSlug, SessionToken, ct);
        return NoContent();
    }

    /// <summary>Newest first: published updates, client-visible daily log summaries, stage progress, photos.</summary>
    [HttpGet("timeline")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildTimelineDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildTimelineDto>> Timeline(
        string companySlug, string projectSlug, [FromQuery] DateTimeOffset? before, [FromQuery] int? take, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetTimelineAsync(companySlug, projectSlug, SessionToken, before, take, ct));
    }

    /// <summary>Client-visible site photos (signed URLs valid ~15 minutes).</summary>
    [HttpGet("photos")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildPhotosDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildPhotosDto>> Photos(
        string companySlug, string projectSlug, [FromQuery] int? skip, [FromQuery] int? take, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetPhotosAsync(companySlug, projectSlug, SessionToken, skip, take, ct));
    }

    /// <summary>Progress curves (actual from daily snapshots, planned, forecast when enabled).</summary>
    [HttpGet("progress")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildProgressDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildProgressDto>> Progress(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetProgressAsync(companySlug, projectSlug, SessionToken, ct));
    }

    // ── v1.1: change orders, selections, measurements ─────────────────────────

    /// <summary>Change orders sent to the client (pending) and decided ones (flag changeOrders).</summary>
    [HttpGet("change-orders")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildChangeOrdersDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildChangeOrdersDto>> ChangeOrders(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetChangeOrdersAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>The client approves/rejects a pending change order — password portal with a session only.</summary>
    [HttpPost("change-orders/{publicId:guid}/decision")]
    [EnableRateLimiting("build-portal-decision")]
    [ProducesResponseType(typeof(PublicBuildChangeOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildChangeOrderDto>> DecideChangeOrder(string companySlug, string projectSlug, Guid publicId,
        [FromBody] PublicBuildChangeOrderDecisionRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.DecideChangeOrderAsync(companySlug, projectSlug, SessionToken, publicId, request, ct));
    }

    /// <summary>The client's selections and their options (flag selections).</summary>
    [HttpGet("selections")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildSelectionsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildSelectionsDto>> Selections(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetSelectionsAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>The client chooses an option — password portal with a session, selection open, before the deadline.</summary>
    [HttpPost("selections/{publicId:guid}/choice")]
    [EnableRateLimiting("build-portal-decision")]
    [ProducesResponseType(typeof(PublicBuildSelectionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildSelectionDto>> ChooseSelection(string companySlug, string projectSlug, Guid publicId,
        [FromBody] PublicBuildSelectionChoiceRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.ChooseSelectionAsync(companySlug, projectSlug, SessionToken, publicId, request, ct));
    }

    /// <summary>Approved measurements (flag measurements; values only with financialSummary).</summary>
    [HttpGet("measurements")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildMeasurementsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildMeasurementsDto>> Measurements(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetMeasurementsAsync(companySlug, projectSlug, SessionToken, ct));
    }

    // ── v1.2: "Meu Imóvel" — handover, warranty, technical assistance ────────

    /// <summary>The handover: status, dates, documents (signed URLs), walkthrough summary (flag postConstruction).</summary>
    [HttpGet("handover")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildHandoverDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildHandoverDto>> Handover(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetHandoverAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>The client accepts the handover — password portal with a session only; once.</summary>
    [HttpPost("handover/accept")]
    [EnableRateLimiting("build-portal-decision")]
    [ProducesResponseType(typeof(PublicBuildHandoverDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildHandoverDto>> AcceptHandover(string companySlug, string projectSlug,
        [FromBody] PublicBuildHandoverAcceptRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.AcceptHandoverAsync(companySlug, projectSlug, SessionToken, request, ct));
    }

    /// <summary>Warranty terms (started on the delivery) with expiry and whether active today.</summary>
    [HttpGet("warranty")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildWarrantyDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildWarrantyDto>> Warranty(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetWarrantyAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>The project's technical assistance requests (status and timeline) and the accepted categories.</summary>
    [HttpGet("assistance")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildAssistanceListDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildAssistanceListDto>> Assistance(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetAssistanceAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>The client opens a request — password portal with a session, after the delivery.</summary>
    [HttpPost("assistance")]
    [EnableRateLimiting("build-portal-decision")]
    [ProducesResponseType(typeof(PublicBuildAssistanceDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PublicBuildAssistanceDto>> OpenAssistance(string companySlug, string projectSlug,
        [FromBody] PublicBuildAssistanceOpenRequest? request, CancellationToken ct)
    {
        NoStore();
        var dto = await _portal.OpenAssistanceAsync(companySlug, projectSlug, SessionToken, request, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    /// <summary>
    /// Adds one photo (multipart field <c>file</c>): JPEG, PNG or WebP, up to 8 MB, checked by its first bytes; at most 6 per
    /// request. Stored privately; only signed URLs come back.
    /// </summary>
    [HttpPost("assistance/{publicId:guid}/photos")]
    [EnableRateLimiting("build-portal-decision")]
    [RequestSizeLimit(MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(PublicBuildAssistanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(PublicBuildErrorDto), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicBuildAssistanceDto>> AddAssistancePhoto(string companySlug, string projectSlug, Guid publicId,
        IFormFile? file, CancellationToken ct)
    {
        NoStore();
        var target = await _portal.AuthorizeAssistanceUploadAsync(companySlug, projectSlug, SessionToken, publicId, ct);
        if (file is null || file.Length == 0)
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidFileType, "Envie uma imagem no campo file.");
        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!ImageExtensions.TryGetValue(contentType, out var extension))
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidFileType, "Envie uma imagem JPEG, PNG ou WebP.");
        if (file.Length > MaxUploadBytes)
            throw new BuildPortalAccessException(413, PublicBuildPortal.CodeFileTooLarge, "A imagem pode ter até 8 MB.");
        if (!await Nexo.Api.Controllers.Integrations.FileSignature.MatchesAsync(file, contentType, ct))
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidFileContent,
                "O conteúdo do arquivo não corresponde a uma imagem válida.");
        await using var stream = file.OpenReadStream();
        return Ok(await _portal.StoreAssistancePhotoAsync(target, stream, contentType, extension, file.Length, ct));
    }

    /// <summary>The client rates a resolved request (1–5), once.</summary>
    [HttpPost("assistance/{publicId:guid}/feedback")]
    [EnableRateLimiting("build-portal-decision")]
    [ProducesResponseType(typeof(PublicBuildAssistanceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildAssistanceDto>> AssistanceFeedback(string companySlug, string projectSlug, Guid publicId,
        [FromBody] PublicBuildAssistanceFeedbackRequest? request, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GiveAssistanceFeedbackAsync(companySlug, projectSlug, SessionToken, publicId, request, ct));
    }

    // ── v1.3: digital twin and reality capture ───────────────────────────────

    /// <summary>
    /// The digital twin (flag model3d): geometry source, signed URL (~15 min) of the viewer GLB — never the original model —,
    /// zones, today's state map and the disclaimer for the geometry source.
    /// </summary>
    [HttpGet("twin")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildTwinDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildTwinDto>> Twin(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetTwinAsync(companySlug, projectSlug, SessionToken, ct));
    }

    /// <summary>State map on a date for the time slider (yyyy-MM-dd, project start … today). ETag; 304 when unchanged.</summary>
    [HttpGet("twin/state")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildTwinStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> TwinState(string companySlug, string projectSlug, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var dto = await _portal.GetTwinStateAsync(companySlug, projectSlug, SessionToken, date, ct);
        return Nexo.Api.Controllers.Modules.Build.BuildTwinController.ETag(this, dto);
    }

    /// <summary>Client-visible site captures (flag realityCapture), newest first, as signed URLs.</summary>
    [HttpGet("reality-captures")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildRealityCapturesDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildRealityCapturesDto>> RealityCaptures(string companySlug, string projectSlug,
        [FromQuery] int? skip, [FromQuery] int? take, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetRealityCapturesAsync(companySlug, projectSlug, SessionToken, skip, take, ct));
    }

    /// <summary>Client-visible project documents (flag documents, v1.4) as signed URLs (~15 min). Never internal fields.</summary>
    [HttpGet("documents")]
    [EnableRateLimiting("build-portal")]
    [ProducesResponseType(typeof(PublicBuildDocumentsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicBuildDocumentsDto>> Documents(string companySlug, string projectSlug, CancellationToken ct)
    {
        NoStore();
        return Ok(await _portal.GetDocumentsAsync(companySlug, projectSlug, SessionToken, ct));
    }

    private const long MaxUploadBytes = 8L * 1024 * 1024;

    private static readonly Dictionary<string, string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"]  = ".png",
        ["image/webp"] = ".webp",
    };

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
