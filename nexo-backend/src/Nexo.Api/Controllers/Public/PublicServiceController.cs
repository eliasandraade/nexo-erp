using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Application.Modules.Service.Public;

namespace Nexo.Api.Controllers.Public;

/// <summary>
/// Public booking portal for Orken Service (sem autenticação). The store is resolved by its public
/// slug (Store.PublicSlug, the same field the restaurant portal uses). No <c>[Authorize]</c> and no
/// RequireServiceModule — but every read/write is scoped to the slug-resolved store and never
/// exposes internal identifiers, costs, commissions or other customers' data.
/// </summary>
[ApiController]
[AllowAnonymous]
public class PublicServiceController : ControllerBase
{
    private readonly PublicServicePortalService _portal;
    private readonly IValidator<CreatePublicAppointmentRequest> _createValidator;

    public PublicServiceController(
        PublicServicePortalService portal,
        IValidator<CreatePublicAppointmentRequest> createValidator)
    {
        _portal = portal;
        _createValidator = createValidator;
    }

    /// <summary>Portal header: store name, vertical preset, labels/capabilities, whether booking is on.</summary>
    [HttpGet("api/public/service/{slug}")]
    public async Task<ActionResult<PublicServicePortalDto>> GetPortal(string slug, CancellationToken ct)
        => Ok(await _portal.GetPortalAsync(slug, ct));

    /// <summary>Active, bookable services. Prices are omitted when the store hides them.</summary>
    [HttpGet("api/public/service/{slug}/catalog")]
    public async Task<ActionResult<IReadOnlyList<PublicServiceCatalogItemDto>>> GetCatalog(
        string slug, CancellationToken ct)
        => Ok(await _portal.GetCatalogAsync(slug, ct));

    /// <summary>Active professionals (public-safe fields only).</summary>
    [HttpGet("api/public/service/{slug}/professionals")]
    public async Task<ActionResult<IReadOnlyList<PublicServiceProfessionalDto>>> GetProfessionals(
        string slug, CancellationToken ct)
        => Ok(await _portal.GetProfessionalsAsync(slug, ct));

    /// <summary>
    /// Real free slots for a chosen service + professional, computed from the professional's working
    /// hours minus existing appointments. Empty (and the portal shows "indisponível") when no hours
    /// are configured — never a fabricated slot.
    /// </summary>
    [HttpGet("api/public/service/{slug}/availability")]
    public async Task<ActionResult<PublicAvailabilityDto>> GetAvailability(
        string slug,
        [FromQuery] Guid catalogItemId,
        [FromQuery] Guid professionalId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
        => Ok(await _portal.GetAvailabilityAsync(slug, catalogItemId, professionalId, from, to, ct));

    /// <summary>
    /// Creates a public appointment (status Scheduled, or Confirmed when the store auto-confirms).
    /// Resolves/creates the customer from the phone and creates a subject when the preset/service
    /// requires it. Never creates a payment, order or package. Rate-limited per client.
    /// </summary>
    [HttpPost("api/public/service/{slug}/appointments")]
    [EnableRateLimiting("public-booking")]
    public async Task<ActionResult<PublicAppointmentCreatedDto>> CreateAppointment(
        string slug, [FromBody] CreatePublicAppointmentRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _portal.CreateAppointmentAsync(slug, request, ct);
        return Created($"/api/public/service/{slug}/appointments/{dto.AppointmentId}", dto);
    }
}
