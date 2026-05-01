using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/coupons")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly CouponService _svc;
    public CouponsController(CouponService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _svc.GetAllAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCouponRequest req, CancellationToken ct)
        => Ok(await _svc.CreateAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCouponRequest req, CancellationToken ct)
        => Ok(await _svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await _svc.RevokeAsync(id, ct);
        return NoContent();
    }
}
