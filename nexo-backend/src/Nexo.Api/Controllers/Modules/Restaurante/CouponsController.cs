using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/coupons")]
[RequireModule("restaurante")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly CouponService _svc;
    public CouponsController(CouponService svc) => _svc = svc;

    [HttpGet]
    [Authorize(Roles = AppRoles.Floor)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _svc.GetAllAsync(ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCouponRequest req, CancellationToken ct)
        => Ok(await _svc.CreateAsync(req, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCouponRequest req, CancellationToken ct)
        => Ok(await _svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await _svc.RevokeAsync(id, ct);
        return NoContent();
    }
}
