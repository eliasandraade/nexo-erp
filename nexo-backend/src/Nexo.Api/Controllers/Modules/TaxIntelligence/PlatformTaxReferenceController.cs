using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Private platform documents (professional reviews, catalogue sources): uploaded, listed, downloaded with authorisation, revoked — never deleted.</summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxDocumentsController : ControllerBase
{
    private readonly TaxDocumentService _documents;

    public PlatformTaxDocumentsController(TaxDocumentService service) => _documents = service;

    /// <summary>Uploads a private document (PDF, image or XML). The hash is the SHA-256 of the bytes received.</summary>
    [HttpPost("documents")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    [RequestSizeLimit(Nexo.Infrastructure.TaxIntelligence.Files.TaxFileOptions.MaxRequestBytes)]
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
}

/// <summary>The tax classification catalogue (cClassTrib): imported as drafts from a documented source, activated explicitly, reviewed apart.</summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxClassificationsController : ControllerBase
{
    private readonly TaxClassificationService _classifications;

    public PlatformTaxClassificationsController(TaxClassificationService service) => _classifications = service;

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
}

/// <summary>The ancillary-obligation catalogue companies assess against.</summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxObligationsController : ControllerBase
{
    private readonly TaxComplianceService _compliance;

    public PlatformTaxObligationsController(TaxComplianceService service) => _compliance = service;

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
}

/// <summary>Default retention policies of the platform — there is no automatic disposal.</summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxRetentionController : ControllerBase
{
    private readonly TaxRetentionService _retention;

    public PlatformTaxRetentionController(TaxRetentionService service) => _retention = service;

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
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "no-store";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        headers["X-Content-SHA256"] = file.Sha256;
        return controller.File(file.Content, file.ContentType, file.FileName);
    }

    public static IActionResult Csv(this ControllerBase controller, (string FileName, byte[] Content) file)
    {
        controller.Response.Headers.CacheControl = "no-store";
        return controller.File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}
