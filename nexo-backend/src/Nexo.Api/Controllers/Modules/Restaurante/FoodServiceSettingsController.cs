using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Authorize]
[RequireModule("restaurante")]
[Route("api/restaurante/settings")]
public class FoodServiceSettingsController : ControllerBase
{
    private readonly FoodServiceSettingsService _service;
    public FoodServiceSettingsController(FoodServiceSettingsService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await _service.GetOrCreateAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateFoodServiceSettingsRequest req, CancellationToken ct)
        => Ok(await _service.UpdateAsync(req, ct));

    [HttpPut("portal")]
    public async Task<IActionResult> UpdatePortalInfo([FromBody] UpdatePortalInfoRequest req, CancellationToken ct)
        => Ok(await _service.UpdatePortalInfoAsync(req, ct));

    [HttpPut("costs")]
    public async Task<IActionResult> UpdateCosts(
        [FromBody] UpdateOperationalCostsRequest req, CancellationToken ct)
        => Ok(await _service.UpdateOperationalCostsAsync(req, ct));
}
