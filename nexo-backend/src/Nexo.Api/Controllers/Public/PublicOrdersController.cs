using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Public;

/// <summary>
/// Endpoints públicos (sem autenticação).
/// Não há RequireModule nem Authorize — qualquer pessoa pode acessar.
/// </summary>
[ApiController]
[AllowAnonymous]
public class PublicOrdersController : ControllerBase
{
    private readonly DeliveryOrderService _orders;
    private readonly PublicMenuService    _menu;

    public PublicOrdersController(DeliveryOrderService orders, PublicMenuService menu)
    {
        _orders = orders;
        _menu   = menu;
    }

    // ── Menu ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna o cardápio público de uma loja pelo slug.
    /// Inclui configurações do portal (AcceptingOrders, DeliveryEnabled, TakeawayEnabled).
    /// </summary>
    [HttpGet("api/public/menu/{slug}")]
    public async Task<ActionResult<PublicMenuDto>> GetMenu(string slug, CancellationToken ct)
        => Ok(await _menu.GetMenuAsync(slug, ct));

    // ── Orders ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna o status atual do pedido pelo token de rastreamento.
    /// </summary>
    [HttpGet("api/public/orders/{trackingToken}")]
    public async Task<ActionResult<DeliveryOrderTrackingDto>> Track(
        string trackingToken, CancellationToken ct)
        => Ok(await _orders.GetByTrackingTokenPublicAsync(trackingToken, ct));

    /// <summary>
    /// Cria um pedido via portal público (sem autenticação).
    /// A loja é resolvida pelo PublicSlug; preços são sempre lidos do catálogo.
    /// </summary>
    [HttpPost("api/public/orders")]
    public async Task<ActionResult<DeliveryOrderDto>> CreateOrder(
        CreatePortalOrderRequest request, CancellationToken ct)
    {
        var result = await _orders.CreateFromPortalAsync(request, ct);
        return CreatedAtAction(nameof(Track), new { trackingToken = result.TrackingToken }, result);
    }
}
