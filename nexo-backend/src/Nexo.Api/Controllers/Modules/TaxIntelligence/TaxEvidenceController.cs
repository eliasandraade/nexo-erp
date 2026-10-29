using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>
/// Private evidence files of the company: uploaded (type checked by its bytes, SHA-256 of the bytes), downloaded only through this API
/// with authorisation on every request, replaced by a newer version or revoked — never deleted, every act audited.
/// </summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = TaxCapabilities.View)]
[Nexo.Api.Filters.ValidateBody]
public class TaxEvidenceController : ControllerBase
{
    private const long MaxRequest = 26 * 1024 * 1024;

    private readonly TaxEvidenceService _evidence;
    private readonly TaxOpportunityEvidenceService _opportunityEvidence;

    public TaxEvidenceController(TaxEvidenceService evidence, TaxOpportunityEvidenceService opportunityEvidence)
    {
        _evidence = evidence;
        _opportunityEvidence = opportunityEvidence;
    }

    /// <summary>Whether private storage is available, the size limit and the accepted file types.</summary>
    [HttpGet("evidence/storage")]
    public ActionResult<TaxFileStorageStatusDto> Storage() => Ok(_evidence.StorageStatus());

    /// <summary>Evidence of an opportunity with its file (PDF, image or XML). The hash is the SHA-256 of the bytes received.</summary>
    [HttpPost("opportunities/{id:guid}/evidence/files")]
    [Authorize(Roles = TaxCapabilities.Analyze)]
    [RequestSizeLimit(MaxRequest)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<TaxOpportunityDto>> UploadOpportunityEvidence(Guid id, IFormFile file, [FromForm] TaxEvidenceType type,
        [FromForm] Guid? requirementId, [FromForm] string description, [FromForm] string? documentReference, [FromForm] string? notes,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await _opportunityEvidence.UploadAsync(id, new TaxUploadMetadata(type, requirementId, description, documentReference, notes),
            stream, file.FileName, ct));
    }

    /// <summary>A newer version of a piece of evidence (with a new file, or only new metadata). The old version stays on record.</summary>
    [HttpPost("opportunities/{id:guid}/evidence/{evidenceId:guid}/replace")]
    [Authorize(Roles = TaxCapabilities.Analyze)]
    [RequestSizeLimit(MaxRequest)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<TaxOpportunityDto>> ReplaceOpportunityEvidence(Guid id, Guid evidenceId, IFormFile? file,
        [FromForm] string description, [FromForm] string? documentReference, [FromForm] string? notes, CancellationToken ct)
    {
        await using var stream = file?.OpenReadStream();
        return Ok(await _opportunityEvidence.ReplaceAsync(id, evidenceId, new ReplaceEvidenceRequest(description, documentReference, notes),
            stream, file?.FileName, ct));
    }

    /// <summary>Withdraws a piece of evidence on record; it supports nothing from then on, and a frozen dossier keeps it unchanged.</summary>
    [HttpPost("opportunities/{id:guid}/evidence/{evidenceId:guid}/revoke")]
    [Authorize(Roles = TaxCapabilities.Analyze)]
    public async Task<ActionResult<TaxOpportunityDto>> RevokeOpportunityEvidence(Guid id, Guid evidenceId, [FromBody] RevokeEvidenceRequest request,
        CancellationToken ct)
        => Ok(await _opportunityEvidence.RevokeAsync(id, evidenceId, request, ct));

    /// <summary>A file for a credit, a compliance assessment or a split settlement of this company.</summary>
    [HttpPost("evidence-files")]
    [Authorize(Roles = TaxCapabilities.Analyze)]
    [RequestSizeLimit(MaxRequest)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<TaxEvidenceFileDto>> Upload(IFormFile file, [FromForm] TaxFileOwner owner, [FromForm] Guid ownerId,
        [FromForm] TaxEvidenceType type, [FromForm] string? description, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await _evidence.UploadAsync(owner, ownerId, type, description, stream, file.FileName, ct));
    }

    [HttpGet("evidence-files")]
    public async Task<ActionResult<IReadOnlyList<TaxEvidenceFileDto>>> List([FromQuery] TaxFileOwner owner, [FromQuery] Guid ownerId, CancellationToken ct)
        => Ok(await _evidence.ListAsync(owner, ownerId, ct));

    /// <summary>The file's bytes — authorised on every request, served as an attachment, never by a URL. Revoked files: the board only.</summary>
    [HttpGet("evidence-files/{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
        => this.PrivateFile(await _evidence.DownloadAsync(id, User.IsInRole(AppRoles.Directors), ct));

    [HttpPost("evidence-files/{id:guid}/revoke")]
    [Authorize(Roles = TaxCapabilities.Analyze)]
    public async Task<ActionResult<TaxEvidenceFileDto>> Revoke(Guid id, [FromBody] RevokeTaxFileRequest request, CancellationToken ct)
        => Ok(await _evidence.RevokeAsync(id, request, ct));

    /// <summary>Places or lifts a legal hold: while held, no retention policy makes the file eligible for disposal.</summary>
    [HttpPost("evidence-files/{id:guid}/legal-hold")]
    [Authorize(Roles = TaxCapabilities.Policies)]
    public async Task<ActionResult<TaxEvidenceFileDto>> LegalHold(Guid id, [FromBody] SetLegalHoldRequest request, CancellationToken ct)
        => Ok(await _evidence.SetLegalHoldAsync(id, request, ct));
}

/// <summary>Company policies: materiality (versioned by date) and evidence retention (preview only — nothing is ever discarded automatically).</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = TaxCapabilities.View)]
[Nexo.Api.Filters.ValidateBody]
public class TaxPoliciesController : ControllerBase
{
    private readonly TaxMaterialityService _materiality;
    private readonly TaxRetentionService _retention;

    public TaxPoliciesController(TaxMaterialityService materiality, TaxRetentionService retention)
    {
        _materiality = materiality;
        _retention = retention;
    }

    [HttpGet("materiality")]
    public async Task<ActionResult<TaxMaterialityDto>> Materiality(CancellationToken ct) => Ok(await _materiality.GetAsync(TaxToday.Brazil(), ct));

    /// <summary>A new materiality version from a date (today or later). Past decisions keep the version they used.</summary>
    [HttpPut("materiality")]
    [Authorize(Roles = TaxCapabilities.Policies)]
    public async Task<ActionResult<TaxMaterialityDto>> ConfigureMateriality([FromBody] ConfigureMaterialityRequest request, CancellationToken ct)
        => Ok(await _materiality.ConfigureAsync(request, TaxToday.Brazil(), ct));

    [HttpGet("retention-policies")]
    public async Task<ActionResult<IReadOnlyList<TaxRetentionPolicyDto>>> RetentionPolicies(CancellationToken ct)
        => Ok(await _retention.ListAsync(platform: false, ct));

    /// <summary>The company's own period for a type, where the platform default allows it.</summary>
    [HttpPut("retention-policies")]
    [Authorize(Roles = TaxCapabilities.Policies)]
    public async Task<ActionResult<TaxRetentionPolicyDto>> ConfigureRetention([FromBody] ConfigureRetentionPolicyRequest request, CancellationToken ct)
        => Ok(await _retention.ConfigureAsync(request, platform: false, ct));

    /// <summary>What a valid policy would make eligible for disposal — a preview: nothing is discarded, legal holds always win.</summary>
    [HttpGet("retention-policies/preview")]
    [Authorize(Roles = TaxCapabilities.Policies)]
    public async Task<ActionResult<TaxRetentionPreviewDto>> RetentionPreview([FromQuery] int take = 200, CancellationToken ct = default)
        => Ok(await _retention.PreviewAsync(TaxToday.Brazil(), take, ct));
}

/// <summary>How the company classifies its products for IBS/CBS (read by sales calculations), and the codes available in the catalogue.</summary>
[ApiController]
[Route("api/tax/classifications")]
[Authorize(Roles = TaxCapabilities.View)]
[Nexo.Api.Filters.ValidateBody]
public class TaxClassificationController : ControllerBase
{
    private readonly TaxClassificationService _classifications;

    public TaxClassificationController(TaxClassificationService classifications) => _classifications = classifications;

    [HttpGet("items")]
    public async Task<ActionResult<TaxPage<TaxItemClassificationDto>>> Items([FromQuery] string? search, [FromQuery] bool onlyMissing = false,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _classifications.ListItemsAsync(search, onlyMissing, skip, take, TaxToday.Brazil(), ct));

    [HttpPut("items/{productId:guid}")]
    [Authorize(Roles = TaxCapabilities.Classify)]
    public async Task<ActionResult<TaxItemClassificationDto>> SetItem(Guid productId, [FromBody] SetItemClassificationRequest request, CancellationToken ct)
        => Ok(await _classifications.SetItemAsync(productId, request, TaxToday.Brazil(), ct));

    /// <summary>Codes of the catalogue (for pickers); only Active ones are accepted by the coverage matrix.</summary>
    [HttpGet("codes")]
    public async Task<ActionResult<TaxPage<TaxClassificationCodeDto>>> Codes([FromQuery] TaxClassificationKind kind = TaxClassificationKind.CClassTrib,
        [FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _classifications.ListAsync(kind, TaxClassificationStatus.Active, search, skip, take, ct));
}
