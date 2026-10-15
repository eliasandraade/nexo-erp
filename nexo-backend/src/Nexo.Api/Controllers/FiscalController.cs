using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Fiscal;
using Nexo.Infrastructure.Fiscal;

namespace Nexo.Api.Controllers;

/// <summary>
/// Fiscal foundation (PR 66): the store's issuer profile, product fiscal data and a readiness
/// checklist. Nothing here issues a nota or talks to SEFAZ.
/// </summary>
[ApiController]
[Route("api/fiscal")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class FiscalController : ControllerBase
{
    private readonly FiscalService _service;
    private readonly ICurrentTenant _tenant;

    public FiscalController(FiscalService service, ICurrentTenant tenant)
    {
        _service = service;
        _tenant  = tenant;
    }

    /// <summary>The issuer profile of the current store; 204 when not filled in yet.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<FiscalProfileDto>> GetProfile(CancellationToken ct)
    {
        var profile = await _service.GetProfileAsync(ct);
        return profile is null ? NoContent() : Ok(profile);
    }

    [HttpPut("profile")]
    public async Task<ActionResult<FiscalProfileDto>> SaveProfile([FromBody] SaveFiscalProfileRequest request, CancellationToken ct)
        => Ok(await _service.SaveProfileAsync(_tenant.Id, request, ct));

    [HttpGet("readiness")]
    public async Task<ActionResult<FiscalReadinessDto>> GetReadiness(CancellationToken ct)
        => Ok(await _service.GetReadinessAsync(ct));

    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<ProductFiscalDto>> GetProduct(Guid id, CancellationToken ct)
        => Ok(await _service.GetProductAsync(id, ct));

    [HttpPut("products/{id:guid}")]
    public async Task<ActionResult<ProductFiscalDto>> UpdateProduct(Guid id, [FromBody] UpdateProductFiscalRequest request, CancellationToken ct)
        => Ok(await _service.UpdateProductAsync(id, request, ct));

    [HttpGet("customers/{id:guid}")]
    public async Task<ActionResult<CustomerFiscalDto>> GetCustomer(Guid id, CancellationToken ct)
        => Ok(await _service.GetCustomerAsync(id, ct));

    [HttpPut("customers/{id:guid}")]
    public async Task<ActionResult<CustomerFiscalDto>> UpdateCustomer(Guid id, [FromBody] UpdateCustomerFiscalRequest request, CancellationToken ct)
        => Ok(await _service.UpdateCustomerAsync(id, request, ct));
}
