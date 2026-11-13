using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Api.Uploads;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Orken Build — Wave 4 reality capture (photos, 360°, video, drone, point cloud). Company side (Management, module build).
/// Files are private; only signed URLs come back. Limits per family: images 25 MB, videos 200 MB, point clouds 500 MB
/// (Build:RealityCapture:Max*Mb). Hidden from the client portal until marked client-visible.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[ValidateBody]
[BuildFileErrors]
public class BuildRealityCapturesController : ControllerBase
{
    private readonly BuildRealityCaptureService _captures;
    private readonly UploadAdmission _uploads;

    public BuildRealityCapturesController(BuildRealityCaptureService captures, UploadAdmission uploads)
    {
        _captures = captures;
        _uploads = uploads;
    }

    [HttpGet("projects/{projectId:guid}/reality-captures")]
    public async Task<ActionResult<IReadOnlyList<BuildRealityCaptureDto>>> List(Guid projectId, [FromQuery] string? type, [FromQuery] Guid? stageId,
        [FromQuery] Guid? zoneId, [FromQuery] string? elementExternalId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await _captures.ListAsync(projectId, type, stageId, zoneId, elementExternalId, from, to, ct));

    /// <summary>
    /// Multipart: <c>file</c>, <c>type</c> (Photo, Photo360, Video, Drone, PointCloud), <c>title</c>, optional <c>capturedAt</c>,
    /// <c>stageId</c>, <c>zoneId</c>, <c>elementExternalId</c>, <c>notes</c>, <c>clientVisible</c>. The content is checked by its
    /// first bytes (JPEG/PNG/WebP, MP4/MOV/WebM, LAS/E57). The query's optional <c>type</c> applies that type's limit before the body
    /// is read; without it, the largest limit applies until the file's family is known.
    /// </summary>
    [HttpPost("projects/{projectId:guid}/reality-captures")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(UploadAdmission.RateLimitPolicy)]
    [DisableFormValueModelBinding]
    [MultipartFileBody("file", "type", "title", "capturedAt", "stageId", "zoneId", "elementExternalId", "notes", "clientVisible")]
    public async Task<ActionResult<BuildRealityCaptureDto>> Create(Guid projectId, [FromQuery(Name = "type")] string? declaredType, CancellationToken ct)
    {
        var maxBytes = await _captures.EnsureCanUploadAsync(projectId, declaredType, ct);
        var read = await _uploads.ReadAsync(HttpContext, maxBytes + UploadAdmission.MultipartOverheadBytes,
            $"Arquivo grande demais: o limite é {maxBytes / (1024 * 1024)} MB.", ct);
        if (read.Refusal is { } refusal) return refusal.ToResult(HttpContext);

        var fields = new CaptureFields();
        if (!await TryUpdateModelAsync(fields, "", new FormValueProvider(BindingSource.Form, read.Form!, CultureInfo.CurrentCulture)))
            return ValidationProblem(ModelState);

        await using var buffer = await BuildTwinController.BufferAsync(read.Form!, maxBytes, ct);
        var dto = await _captures.CreateAsync(projectId,
            new BuildRealityCaptureService.CreateCapture(fields.Type ?? declaredType ?? "", fields.Title ?? "", fields.CapturedAt, fields.StageId,
                fields.ZoneId, fields.ElementExternalId, fields.Notes, fields.ClientVisible ?? false), buffer, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    /// <summary>The text fields of the form, converted by the framework's binders once the form has been read.</summary>
    private sealed class CaptureFields
    {
        public string? Type { get; set; }
        public string? Title { get; set; }
        public DateTimeOffset? CapturedAt { get; set; }
        public Guid? StageId { get; set; }
        public Guid? ZoneId { get; set; }
        public string? ElementExternalId { get; set; }
        public string? Notes { get; set; }
        public bool? ClientVisible { get; set; }
    }

    [HttpGet("reality-captures/{id:guid}")]
    public async Task<ActionResult<BuildRealityCaptureDto>> Get(Guid id, CancellationToken ct) => Ok(await _captures.GetAsync(id, ct));

    [HttpPut("reality-captures/{id:guid}")]
    public async Task<ActionResult<BuildRealityCaptureDto>> Update(Guid id, [FromBody] UpdateBuildRealityCaptureRequest request, CancellationToken ct)
        => Ok(await _captures.UpdateAsync(id, request, ct));

    [HttpPut("reality-captures/{id:guid}/client-visibility")]
    public async Task<ActionResult<BuildRealityCaptureDto>> SetVisibility(Guid id, [FromBody] SetRealityCaptureVisibilityRequest request,
        CancellationToken ct)
        => Ok(await _captures.SetVisibilityAsync(id, request.ClientVisible, ct));

    [HttpDelete("reality-captures/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _captures.DeleteAsync(id, ct);
        return NoContent();
    }
}
