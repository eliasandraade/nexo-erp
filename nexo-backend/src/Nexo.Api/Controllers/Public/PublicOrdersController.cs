using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Public;

/// <summary>
/// Endpoints públicos (sem autenticação) para rastreamento de pedidos pelo cliente.
/// Não há RequireModule nem Authorize — qualquer pessoa com o tracking token pode consultar.
/// </summary>
[ApiController]
[Route("api/public/orders")]
[AllowAnonymous]
public class PublicOrdersController : ControllerBase
{
    private readonly DeliveryOrderService _service;

    public PublicOrdersController(DeliveryOrderService service) => _service = service;

    /// <summary>
    /// Retorna o status atual do pedido pelo token de rastreamento.
    /// O token é gerado no momento da criação do pedido e enviado ao cliente.
    /// </summary>
    [HttpGet("{trackingToken}")]
    public async Task<ActionResult<DeliveryOrderTrackingDto>> Track(
        string trackingToken, CancellationToken ct)
        => Ok(await _service.GetByTrackingTokenPublicAsync(trackingToken, ct));

    /// <summary>
    /// Cria um pedido via portal público (sem autenticação).
    /// A loja é resolvida pelo PublicSlug; preços são sempre lidos do catálogo.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DeliveryOrderDto>> CreateOrder(
        CreatePortalOrderRequest request, CancellationToken ct)
    {
        var result = await _service.CreateFromPortalAsync(request, ct);
        return CreatedAtAction(nameof(Track), new { trackingToken = result.TrackingToken }, result);
    }
}
