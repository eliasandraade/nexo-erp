using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>
/// Reference data kept by the platform: private documents (professional opinions, catalogue sources), the tax classification catalogue,
/// the ancillary-obligation catalogue and the default retention policies. Operators read; administrators change.
/// </summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxReferenceController : ControllerBase
{
    private readonly TaxDocumentService _documents;
    private readonly TaxClassificationService _classifications;
    private readonly TaxComplianceService _compliance;
    private readonly TaxRetentionService _retention;

    public PlatformTaxReferenceController(TaxDocumentService documents, TaxClassificationService classifications, TaxComplianceService compliance,
        TaxRetentionService retention)
    {
        _documents = documents;
        _classifications = classifications;
        _compliance = compliance;
        _retention = retention;
    }

    // ── Documents ───────────────────────────────────────────────────────────────

    /// <summary>Uploads a private document (PDF, image or XML). The hash is the SHA-256 of the bytes received.</summary>
    [HttpPost("documents")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    [RequestSizeLimit(26 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<TaxStoredFileDto>> UploadDocument(IFormFile file, [FromForm] string purpose, [FromForm] string? description,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await _documents.UploadAsync(purpose, description, stream, file.FileName, ct));
    }

    [HttpGet("documents")]
    public async Task<ActionResult<IReadOnlyList<TaxStoredFileDto>>> Documents([FromQuery] string? purpose, CancellationToken ct)
        => Ok(await _documents.ListAsync(purpose, ct));

    /// <summary>Downloads a document — authorised on every request, served as an attachment, never by URL.</summary>
    [HttpGet("documents/{id:guid}/content")]
    public async Task<IActionResult> DocumentContent(Guid id, CancellationToken ct)
        => this.PrivateFile(await _documents.DownloadAsync(id, ct));

    [HttpPost("documents/{id:guid}/revoke")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxStoredFileDto>> RevokeDocument(Guid id, [FromBody] RevokeTaxFileRequest request, CancellationToken ct)
        => Ok(await _documents.RevokeAsync(id, request, ct));

    // ── Classification catalogue ────────────────────────────────────────────────

    [HttpGet("classifications")]
    public async Task<ActionResult<TaxPage<TaxClassificationCodeDto>>> Classifications([FromQuery] TaxClassificationKind? kind,
        [FromQuery] TaxClassificationStatus? status, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _classifications.ListAsync(kind, status, search, skip, take, ct));

    /// <summary>Imports codes from a documented source; they enter as Draft and do nothing until activated.</summary>
    [HttpPost("classifications/import")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxClassificationImportResultDto>> ImportClassifications([FromBody] ImportClassificationCodesRequest request,
        CancellationToken ct)
        => Ok(await _classifications.ImportAsync(request, ct));

    [HttpPost("classifications/{id:guid}/status")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxClassificationCodeDto>> ClassificationStatus(Guid id, [FromBody] ChangeClassificationStatusRequest request,
        CancellationToken ct)
        => Ok(await _classifications.ChangeStatusAsync(id, request, ct));

    [HttpPost("classifications/{id:guid}/professional-review")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxClassificationCodeDto>> ClassificationReview(Guid id, [FromBody] RecordClassificationReviewRequest request,
        CancellationToken ct)
        => Ok(await _classifications.RecordReviewAsync(id, request, ct));

    // ── Obligation catalogue ────────────────────────────────────────────────────

    [HttpGet("obligations")]
    public async Task<ActionResult<IReadOnlyList<TaxObligationDefinitionDto>>> Obligations(CancellationToken ct)
        => Ok(await _compliance.ObligationsAsync(onlyActive: false, ct));

    [HttpPost("obligations")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxObligationDefinitionDto>> DefineObligation([FromBody] CreateObligationDefinitionRequest request, CancellationToken ct)
        => Ok(await _compliance.DefineObligationAsync(request, ct));

    [HttpPost("obligations/{id:guid}/active")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxObligationDefinitionDto>> SetObligationActive(Guid id, [FromBody] SetObligationActiveRequest request,
        CancellationToken ct)
        => Ok(await _compliance.SetObligationActiveAsync(id, request, ct));

    // ── Retention defaults ──────────────────────────────────────────────────────

    [HttpGet("retention-policies")]
    public async Task<ActionResult<IReadOnlyList<TaxRetentionPolicyDto>>> RetentionPolicies(CancellationToken ct)
        => Ok(await _retention.ListAsync(platform: true, ct));

    /// <summary>A new version of the platform default for a type. Never discards anything: there is no automatic disposal.</summary>
    [HttpPut("retention-policies")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxRetentionPolicyDto>> ConfigureRetention([FromBody] ConfigureRetentionPolicyRequest request, CancellationToken ct)
        => Ok(await _retention.ConfigureAsync(request, platform: true, ct));
}

/// <summary>How private tax files are served: as attachments, never sniffed or rendered, never cached, sandboxed.</summary>
public static class TaxFileResults
{
    public static IActionResult PrivateFile(this ControllerBase controller, TaxFileDownload file)
    {
        var headers = controller.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Cache-Control"] = "no-store";
        headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        headers["X-Content-SHA256"] = file.Sha256;
        return controller.File(file.Content, file.ContentType, file.FileName);
    }

    public static IActionResult Csv(this ControllerBase controller, (string FileName, byte[] Content) file)
    {
        controller.Response.Headers["Cache-Control"] = "no-store";
        return controller.File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}
