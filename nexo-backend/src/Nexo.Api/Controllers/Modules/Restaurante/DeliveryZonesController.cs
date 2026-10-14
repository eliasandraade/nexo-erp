using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/delivery-zones")]
[RequireModule("restaurante")]
[Authorize]
public class DeliveryZonesController : ControllerBase
{
    private readonly DeliveryZoneService _svc;
    public DeliveryZonesController(DeliveryZoneService svc) => _svc = svc;

    [HttpGet]
    [Authorize(Roles = AppRoles.Floor)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _svc.GetAllAsync(ct));

    [HttpPut]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertDeliveryZonesRequest req, CancellationToken ct)
        => Ok(await _svc.UpsertAsync(req, ct));
}
