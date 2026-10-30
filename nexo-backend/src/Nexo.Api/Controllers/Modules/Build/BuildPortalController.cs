using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// The company side of the client portal: company slug/branding, each project's portal (slug, access,
/// password, branding, visibility, publication), what of the daily log the client sees, and client updates.
/// Every change is audited. The password is write-only: responses only say whether one is set.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[Nexo.Api.Filters.ValidateBody]
public class BuildPortalController : ControllerBase
{
    private readonly BuildPortalAdminService _portal;

    public BuildPortalController(BuildPortalAdminService portal) => _portal = portal;

    // ── Company ───────────────────────────────────────────────────────────────

    [HttpGet("portal/company")]
    public async Task<ActionResult<BuildPortalCompanyDto>> GetCompany(CancellationToken ct)
        => Ok(await _portal.GetCompanyAsync(ct));

    [HttpPut("portal/company")]
    public async Task<ActionResult<BuildPortalCompanyDto>> UpdateCompany(
        [FromBody] UpdateBuildPortalCompanyRequest request, CancellationToken ct)
        => Ok(await _portal.UpdateCompanyAsync(request, ct));

    // ── Project portal ────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/portal")]
    public async Task<ActionResult<BuildProjectPortalDto>> GetPortal(Guid projectId, CancellationToken ct)
        => Ok(await _portal.GetPortalAsync(projectId, ct));

    [HttpPut("projects/{projectId:guid}/portal")]
    public async Task<ActionResult<BuildProjectPortalDto>> UpdatePortal(
        Guid projectId, [FromBody] UpdateBuildProjectPortalRequest request, CancellationToken ct)
        => Ok(await _portal.UpdatePortalAsync(projectId, request, ct));

    /// <summary>Sets the portal password (≥ 8 characters). Every open session of the portal ends.</summary>
    [HttpPut("projects/{projectId:guid}/portal/password")]
    public async Task<ActionResult<BuildProjectPortalDto>> SetPassword(
        Guid projectId, [FromBody] SetBuildPortalPasswordRequest request, CancellationToken ct)
        => Ok(await _portal.SetPasswordAsync(projectId, request.Password, ct));

    [HttpDelete("projects/{projectId:guid}/portal/password")]
    public async Task<ActionResult<BuildProjectPortalDto>> RemovePassword(Guid projectId, CancellationToken ct)
        => Ok(await _portal.RemovePasswordAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/portal/publish")]
    public async Task<ActionResult<BuildProjectPortalDto>> Publish(Guid projectId, CancellationToken ct)
        => Ok(await _portal.PublishAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/portal/unpublish")]
    public async Task<ActionResult<BuildProjectPortalDto>> Unpublish(Guid projectId, CancellationToken ct)
        => Ok(await _portal.UnpublishAsync(projectId, ct));

    // ── Daily log visibility ──────────────────────────────────────────────────

    /// <summary>Shows a daily log entry to the client (with the summary written for the client) or hides it.</summary>
    [HttpPut("daily-logs/{logId:guid}/client-visibility")]
    public async Task<ActionResult<BuildDailyLogDto>> SetDailyLogVisibility(
        Guid logId, [FromBody] SetDailyLogClientVisibilityRequest request, CancellationToken ct)
        => Ok(await _portal.SetDailyLogVisibilityAsync(logId, request, ct));

    /// <summary>Shows a site photo to the client or hides it (it also needs its daily log visible).</summary>
    [HttpPut("daily-log-photos/{photoId:guid}/client-visibility")]
    public async Task<ActionResult<BuildDailyLogPhotoDto>> SetPhotoVisibility(
        Guid photoId, [FromBody] SetPhotoClientVisibilityRequest request, CancellationToken ct)
        => Ok(await _portal.SetPhotoVisibilityAsync(photoId, request.ClientVisible, ct));

    // ── Client updates ────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/client-updates")]
    public async Task<ActionResult<IReadOnlyList<BuildClientUpdateDto>>> ListUpdates(Guid projectId, CancellationToken ct)
        => Ok(await _portal.ListUpdatesAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/client-updates")]
    public async Task<ActionResult<BuildClientUpdateDto>> CreateUpdate(
        Guid projectId, [FromBody] SaveBuildClientUpdateRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _portal.CreateUpdateAsync(projectId, request, ct));

    [HttpPut("client-updates/{updateId:guid}")]
    public async Task<ActionResult<BuildClientUpdateDto>> EditUpdate(
        Guid updateId, [FromBody] SaveBuildClientUpdateRequest request, CancellationToken ct)
        => Ok(await _portal.EditUpdateAsync(updateId, request, ct));

    [HttpPost("client-updates/{updateId:guid}/publish")]
    public async Task<ActionResult<BuildClientUpdateDto>> PublishUpdate(Guid updateId, CancellationToken ct)
        => Ok(await _portal.PublishUpdateAsync(updateId, ct));

    [HttpPost("client-updates/{updateId:guid}/archive")]
    public async Task<ActionResult<BuildClientUpdateDto>> ArchiveUpdate(Guid updateId, CancellationToken ct)
        => Ok(await _portal.ArchiveUpdateAsync(updateId, ct));
}
