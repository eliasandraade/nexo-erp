using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/delivery-zones")]
[Authorize]
public class DeliveryZonesController : ControllerBase
{
    private readonly DeliveryZoneService _svc;
    public DeliveryZonesController(DeliveryZoneService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _svc.GetAllAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertDeliveryZonesRequest req, CancellationToken ct)
        => Ok(await _svc.UpsertAsync(req, ct));
}
