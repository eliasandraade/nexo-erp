using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Orken Build — Wave 4 "Digital Twin", company side (Management, module build): tenant feature flags (read-only), models and
/// versions (upload, processing, viewer derivative), zones, elements, 4D state on a date, stage bindings, explicit element
/// states, overview and 5D preparation. Uploads and processing require the company's Build3D flag (403 build_3d_disabled).
/// See docs/build/internal-api.md (# Wave 4) and docs/build/digital-twin.md.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[ValidateBody]
[BuildFileErrors]
public class BuildTwinController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BuildModelService _models;
    private readonly BuildTwinService _twin;
    private readonly BuildTenantSettingsService _settings;
    private readonly BuildTwinOptions _options;
    private readonly ICurrentTenant _tenant;

    public BuildTwinController(BuildModelService models, BuildTwinService twin, BuildTenantSettingsService settings,
        BuildTwinOptions options, ICurrentTenant tenant)
    {
        _models = models;
        _twin = twin;
        _settings = settings;
        _options = options;
        _tenant = tenant;
    }

    /// <summary>The company's Build feature flags (Build3D, BuildAI, BuildVision, BuildPredict) — changed only by the platform.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<BuildTenantSettingsDto>> Settings(CancellationToken ct) => Ok(await _settings.GetAsync(_tenant.Id, ct));

    // ── Models ────────────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/models")]
    public async Task<ActionResult<IReadOnlyList<BuildModelDto>>> Models(Guid projectId, CancellationToken ct)
        => Ok(await _models.ListAsync(projectId, ct));

    /// <summary>
    /// New model with its first version. Multipart: <c>file</c>, <c>name</c>, <c>discipline</c> (Arquitetura, Estrutura,
    /// Hidráulica, Elétrica, Outro). 201 with the queued (or ready, for PDF/image) version; 422 model_format_rejected (RVT/DWG,
    /// recorded without storing the file) or another 422 code for refused files (nothing recorded).
    /// </summary>
    [HttpPost("projects/{projectId:guid}/models")]
    [BuildUploadLimit(BuildUploadKind.Model)]
    public async Task<IActionResult> CreateModel(Guid projectId, IFormFile? file, [FromForm] string? name, [FromForm] string? discipline,
        CancellationToken ct)
    {
        await using var buffer = await BufferAsync(file, _options.MaxModelBytes, ct);
        return Upload(await _models.CreateModelAsync(projectId, name, discipline, buffer, ct));
    }

    [HttpGet("models/{modelId:guid}")]
    public async Task<ActionResult<BuildModelDto>> Model(Guid modelId, CancellationToken ct) => Ok(await _models.GetAsync(modelId, ct));

    /// <summary>New version of a model (multipart <c>file</c>); same rules as the first upload.</summary>
    [HttpPost("models/{modelId:guid}/versions")]
    [BuildUploadLimit(BuildUploadKind.Model)]
    public async Task<IActionResult> AddVersion(Guid modelId, IFormFile? file, CancellationToken ct)
    {
        await using var buffer = await BufferAsync(file, _options.MaxModelBytes, ct);
        return Upload(await _models.AddVersionAsync(modelId, buffer, ct));
    }

    [HttpGet("models/versions/{versionId:guid}")]
    public async Task<ActionResult<BuildModelVersionDto>> Version(Guid versionId, CancellationToken ct)
        => Ok(await _models.GetVersionAsync(versionId, ct));

    /// <summary>Signed URL (15 min) of the viewer GLB of the version (never the source file).</summary>
    [HttpGet("models/versions/{versionId:guid}/viewer")]
    public async Task<ActionResult<BuildModelViewerDto>> Viewer(Guid versionId, CancellationToken ct)
        => Ok(await _models.ViewerAsync(versionId, ct));

    /// <summary>
    /// Attaches the viewer derivative converted in the browser (multipart <c>file</c>, GLB whose node names are the IFC GlobalIds).
    /// 422 viewer_mismatch when too few node names are elements of the version.
    /// </summary>
    [HttpPut("models/versions/{versionId:guid}/viewer")]
    [BuildUploadLimit(BuildUploadKind.Model)]
    public async Task<ActionResult<BuildModelVersionDto>> AttachViewer(Guid versionId, IFormFile? file, CancellationToken ct)
    {
        await using var buffer = await BufferAsync(file, _options.MaxModelBytes, ct);
        return Ok(await _models.AttachViewerAsync(versionId, buffer, ct));
    }

    /// <summary>Queues a failed version again.</summary>
    [HttpPost("models/versions/{versionId:guid}/reprocess")]
    public async Task<ActionResult<BuildModelVersionDto>> Reprocess(Guid versionId, CancellationToken ct)
        => Ok(await _models.ReprocessAsync(versionId, ct));

    // ── Elements, zones, state ────────────────────────────────────────────────

    /// <summary>
    /// Paged elements with their state on <paramref name="date"/> (default today). state: a state name or Unbound. zoneId: a zone of
    /// the version or a manual zone of the project (its subtree). externalId: exact ids, comma-separated, up to 200 (422 too_many_ids).
    /// </summary>
    [HttpGet("models/versions/{versionId:guid}/elements")]
    public async Task<ActionResult<BuildModelElementPageDto>> Elements(Guid versionId, [FromQuery] string? category, [FromQuery] Guid? zoneId,
        [FromQuery] string? state, [FromQuery] DateOnly? date, [FromQuery] int? skip, [FromQuery] int? take, [FromQuery] string? externalId,
        CancellationToken ct)
        => Ok(await _twin.ElementsAsync(versionId, category, zoneId, state, date, skip, take, ct, externalId));

    /// <summary>
    /// Signed URL (15 min) of the version's private source file, for the company's own users only (never in the portal). Audited.
    /// 422 source_not_stored (refused format); 503 storage_unavailable.
    /// </summary>
    [HttpGet("models/versions/{versionId:guid}/source")]
    public async Task<ActionResult<BuildModelSourceDto>> Source(Guid versionId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await _models.SourceAsync(versionId, ct));
    }

    /// <summary>State of the project's manual zones on a date (yyyy-MM-dd, default today) through zone bindings — no model version.</summary>
    [HttpGet("projects/{projectId:guid}/twin/zones/state")]
    public async Task<ActionResult<BuildZoneStatesDto>> ZoneStates(Guid projectId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _twin.ZoneStatesAsync(projectId, date, ct));

    [HttpGet("models/versions/{versionId:guid}/elements/{externalId}/events")]
    public async Task<ActionResult<IReadOnlyList<BuildElementEventDto>>> ElementEvents(Guid versionId, string externalId, CancellationToken ct)
        => Ok(await _twin.ElementEventsAsync(versionId, externalId, ct));

    [HttpGet("models/versions/{versionId:guid}/zones")]
    public async Task<ActionResult<IReadOnlyList<BuildModelZoneDto>>> VersionZones(Guid versionId, CancellationToken ct)
        => Ok(await _twin.VersionZonesAsync(versionId, ct));

    /// <summary>Compact 4D state on a date (yyyy-MM-dd, default today) with an ETag (304 when unchanged).</summary>
    [HttpGet("models/versions/{versionId:guid}/state")]
    public async Task<IActionResult> State(Guid versionId, [FromQuery] DateOnly? date, CancellationToken ct)
        => ETagged(await _twin.StateAsync(versionId, date, ct));

    [HttpGet("projects/{projectId:guid}/zones")]
    public async Task<ActionResult<IReadOnlyList<BuildModelZoneDto>>> ProjectZones(Guid projectId, CancellationToken ct)
        => Ok(await _twin.ProjectZonesAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/zones")]
    public async Task<ActionResult<BuildModelZoneDto>> CreateZone(Guid projectId, [FromBody] SaveBuildZoneRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _twin.CreateZoneAsync(projectId, request, ct));

    [HttpPut("zones/{zoneId:guid}")]
    public async Task<ActionResult<BuildModelZoneDto>> UpdateZone(Guid zoneId, [FromBody] SaveBuildZoneRequest request, CancellationToken ct)
        => Ok(await _twin.UpdateZoneAsync(zoneId, request, ct));

    [HttpDelete("zones/{zoneId:guid}")]
    public async Task<IActionResult> DeleteZone(Guid zoneId, CancellationToken ct)
    {
        await _twin.DeleteZoneAsync(zoneId, ct);
        return NoContent();
    }

    // ── Bindings and element states ───────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/twin/bindings")]
    public async Task<ActionResult<IReadOnlyList<BuildModelStageBindingDto>>> Bindings(Guid projectId, CancellationToken ct)
        => Ok(await _twin.BindingsAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/twin/bindings")]
    public async Task<ActionResult<BuildModelStageBindingDto>> CreateBinding(Guid projectId, [FromBody] CreateBuildBindingRequest request,
        CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _twin.CreateBindingAsync(projectId, request, ct));

    /// <summary>Binds every element of a version matching category / zone (with sub-zones) / ids to a stage.</summary>
    [HttpPost("projects/{projectId:guid}/twin/bindings/bulk")]
    public async Task<ActionResult<BulkBuildBindingResultDto>> BulkBind(Guid projectId, [FromBody] BulkBuildBindingRequest request,
        CancellationToken ct)
        => Ok(await _twin.BulkBindAsync(projectId, request, ct));

    [HttpDelete("twin/bindings/{bindingId:guid}")]
    public async Task<IActionResult> DeleteBinding(Guid bindingId, CancellationToken ct)
    {
        await _twin.DeleteBindingAsync(bindingId, ct);
        return NoContent();
    }

    /// <summary>Explicit state of elements from a date on (append-only; overrides what the bindings derive).</summary>
    [HttpPost("projects/{projectId:guid}/twin/element-events")]
    public async Task<ActionResult<IReadOnlyList<BuildElementEventDto>>> RecordEvents(Guid projectId,
        [FromBody] RecordBuildElementEventsRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _twin.RecordEventsAsync(projectId, request, ct));

    // ── Overview and 5D prep ──────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/twin")]
    public async Task<ActionResult<BuildTwinOverviewDto>> Overview(Guid projectId, CancellationToken ct)
        => Ok(await _twin.OverviewAsync(projectId, ct));

    /// <summary>Per stage: budgeted/realized cost up to the date and its bound elements/quantities (rateio por quantidade).</summary>
    [HttpGet("projects/{projectId:guid}/twin/cost")]
    public async Task<ActionResult<BuildTwinCostDto>> Cost(Guid projectId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _twin.CostAsync(projectId, date, ct));

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static async Task<BuildUploadBuffer> BufferAsync(IFormFile? file, long maxBytes, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new Nexo.Domain.Exceptions.DomainException("Envie o arquivo no campo file.", "file_required");
        if (file.Length > maxBytes)
            throw new BuildFileTooLargeException($"Arquivo grande demais: o limite é {maxBytes / (1024 * 1024)} MB.");
        await using var stream = file.OpenReadStream();
        return await BuildUploadBuffer.FromAsync(stream, file.FileName, maxBytes, ct);
    }

    private IActionResult Upload(BuildModelUploadResultDto result)
        => result.Version.Status == nameof(Nexo.Domain.Modules.Build.BuildModelVersionStatus.Rejected)
            ? UnprocessableEntity(new
            {
                error = result.Version.StatusMessage,
                code = "model_format_rejected",
                model = result.Model,
                version = result.Version,
                requestId = HttpContext.TraceIdentifier,
            })
            : StatusCode(StatusCodes.Status201Created, result);

    /// <summary>JSON with a strong ETag of its content; 304 when the client already has it.</summary>
    private IActionResult ETagged<T>(T dto) => ETag(this, dto);

    internal static IActionResult ETag<T>(ControllerBase c, T dto)
    {
        var json = JsonSerializer.Serialize(dto, Json);
        var tag = "\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32].ToLowerInvariant() + "\"";
        c.Response.Headers.ETag = tag;
        c.Response.Headers.CacheControl = "private, no-cache";
        if (c.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',').Select(x => x.Trim()).Contains(tag)))
            return c.StatusCode(StatusCodes.Status304NotModified);
        return c.Content(json, "application/json; charset=utf-8");
    }
}
