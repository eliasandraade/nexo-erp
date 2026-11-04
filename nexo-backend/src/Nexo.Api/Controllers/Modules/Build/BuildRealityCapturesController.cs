using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
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
    private readonly BuildTwinOptions _options;

    public BuildRealityCapturesController(BuildRealityCaptureService captures, BuildTwinOptions options)
    {
        _captures = captures;
        _options = options;
    }

    [HttpGet("projects/{projectId:guid}/reality-captures")]
    public async Task<ActionResult<IReadOnlyList<BuildRealityCaptureDto>>> List(Guid projectId, [FromQuery] string? type, [FromQuery] Guid? stageId,
        [FromQuery] Guid? zoneId, [FromQuery] string? elementExternalId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await _captures.ListAsync(projectId, type, stageId, zoneId, elementExternalId, from, to, ct));

    /// <summary>
    /// Multipart: <c>file</c>, <c>type</c> (Photo, Photo360, Video, Drone, PointCloud), <c>title</c>, optional <c>capturedAt</c>,
    /// <c>stageId</c>, <c>zoneId</c>, <c>elementExternalId</c>, <c>notes</c>, <c>clientVisible</c>. The content is checked by its
    /// first bytes (JPEG/PNG/WebP, MP4/MOV/WebM, LAS/E57).
    /// </summary>
    [HttpPost("projects/{projectId:guid}/reality-captures")]
    [BuildUploadLimit(BuildUploadKind.RealityCapture)]
    public async Task<ActionResult<BuildRealityCaptureDto>> Create(Guid projectId, IFormFile? file, [FromForm] string? type,
        [FromForm] string? title, [FromForm] DateTimeOffset? capturedAt, [FromForm] Guid? stageId, [FromForm] Guid? zoneId,
        [FromForm] string? elementExternalId, [FromForm] string? notes, [FromForm] bool? clientVisible, CancellationToken ct)
    {
        await using var buffer = await BuildTwinController.BufferAsync(file, _options.MaxCaptureBytes, ct);
        var dto = await _captures.CreateAsync(projectId,
            new BuildRealityCaptureService.CreateCapture(type ?? "", title ?? "", capturedAt, stageId, zoneId, elementExternalId, notes,
                clientVisible ?? false), buffer, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
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
