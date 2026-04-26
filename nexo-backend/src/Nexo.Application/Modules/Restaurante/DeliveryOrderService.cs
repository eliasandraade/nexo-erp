using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

/// <summary>
/// Gerencia o ciclo de vida dos pedidos externos (DeliveryOrders).
///
/// Princípio central: nenhum pedido externo cria RestOrder diretamente.
/// Todo pedido externo entra como RestDeliveryOrder (Status=Received).
/// RestOrder só é criado em AcceptAsync, pelo operador.
///
/// Fontes de criação:
///   CreateAsync          → genérico (iFood webhooks, integrações externas)
///   CreateManualAsync    → operador autenticado (telefone, balcão, WhatsApp)
///   CreateFromPortalAsync → cliente via portal público (sem auth, store por PublicSlug)
///
/// Implementa IDeliveryOrderSyncService para receber atualizações de status
/// do OrderService de forma unidirecional (RestOrder.Status → DeliveryOrder.Status).
/// </summary>
public class DeliveryOrderService : IDeliveryOrderSyncService
{
    private readonly IDeliveryOrderRepository      _repo;
    private readonly IOrderRepository              _orders;
    private readonly ICurrentTenant                _currentTenant;
    private readonly ICurrentUser                  _currentUser;
    private readonly IProductRepository            _products;
    private readonly IStoreRepository              _stores;
    private readonly IModifierGroupRepository      _modifierGroups;
    private readonly IFoodServiceSettingsRepository _settings;
    private readonly ILogger<DeliveryOrderService> _logger;

    public DeliveryOrderService(
        IDeliveryOrderRepository       repo,
        IOrderRepository               orders,
        ICurrentTenant                 currentTenant,
        ICurrentUser                   currentUser,
        IProductRepository             products,
        IStoreRepository               stores,
        IModifierGroupRepository       modifierGroups,
        IFoodServiceSettingsRepository settings,
        ILogger<DeliveryOrderService>  logger)
    {
        _repo           = repo;
        _orders         = orders;
        _currentTenant  = currentTenant;
        _currentUser    = currentUser;
        _products       = products;
        _stores         = stores;
        _modifierGroups = modifierGroups;
        _settings       = settings;
        _logger         = logger;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DeliveryOrderDto>> GetAllAsync(
        string[]? statuses = null,
        string[]? channels = null,
        DateOnly? date = null,
        CancellationToken ct = default)
    {
        var statusFilters = statuses?
            .Select(s => Enum.Parse<DeliveryOrderStatus>(s, ignoreCase: true))
            .ToArray();
        var channelFilters = channels?
            .Select(c => Enum.Parse<DeliveryChannel>(c, ignoreCase: true))
            .ToArray();

        var list = await _repo.GetAllAsync(statusFilters, channelFilters, date, ct);
        return list.Select(Map).ToList();
    }

    public async Task<DeliveryOrderDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("DeliveryOrder", id);
        return Map(order);
    }

    public async Task<DeliveryOrderTrackingDto> GetByTrackingTokenAsync(
        string token, CancellationToken ct = default)
    {
        var order = await _repo.GetByTrackingTokenAsync(token, ct)
            ?? throw new NotFoundException("DeliveryOrder", token);
        return MapTracking(order);
    }

    /// <summary>
    /// Variante pública: ignora query filters de tenant/store.
    /// Usada pelo endpoint público /api/public/orders/{token} onde não há contexto de auth.
    /// </summary>
    public async Task<DeliveryOrderTrackingDto> GetByTrackingTokenPublicAsync(
        string token, CancellationToken ct = default)
    {
        var order = await _repo.GetByTrackingTokenPublicAsync(token, ct)
            ?? throw new NotFoundException("DeliveryOrder", token);
        return MapTracking(order);
    }

    // ── Create (generic — external integrations / iFood webhooks) ─────────────

    public async Task<DeliveryOrderDto> CreateAsync(
        CreateDeliveryOrderRequest request, CancellationToken ct = default)
    {
        var channel   = Enum.Parse<DeliveryChannel>(request.Channel, ignoreCase: true);
        var orderType = Enum.Parse<DeliveryOrderType>(request.OrderType, ignoreCase: true);

        var order = RestDeliveryOrder.Create(
            tenantId:            _currentTenant.Id,
            orderNumber:         0,
            channel:             channel,
            orderType:           orderType,
            customerName:        request.CustomerName,
            customerPhone:       request.CustomerPhone,
            customerEmail:       request.CustomerEmail,
            customerId:          request.CustomerId,
            deliveryAddressJson: request.DeliveryAddressJson,
            deliveryFee:         request.DeliveryFee,
            estimatedMinutes:    request.EstimatedMinutes,
            notes:               request.Notes,
            externalOrderId:     request.ExternalOrderId,
            externalEventType:   request.ExternalEventType,
            rawPayload:          request.RawPayload);

        await SaveOrderWithRetryAsync(order, _repo.GetNextOrderNumberAsync, ct);

        foreach (var itemReq in request.Items ?? [])
        {
            var item = order.AddItem(
                _currentTenant.Id,
                itemReq.ProductName,
                itemReq.UnitPrice,
                itemReq.Quantity,
                itemReq.ProductId,
                notes: itemReq.Notes);
            _repo.TrackItem(item);

            foreach (var modReq in itemReq.Modifiers ?? [])
            {
                var modifier = item.AddModifier(
                    _currentTenant.Id,
                    modReq.Label,
                    modReq.Price,
                    modReq.ModifierId);
                _repo.TrackModifier(modifier);
            }
        }

        await _repo.SaveChangesAsync(ct);
        _logger.LogInformation("DeliveryOrder #{Number} criado via {Channel} (Id: {Id})",
            order.OrderNumber, channel, order.Id);
        return Map(order);
    }

    // ── CreateManual (operador autenticado — telefone, balcão, WhatsApp) ──────

    /// <summary>
    /// Cria pedido manual pelo operador. Produtos resolvidos do catálogo —
    /// snapshot de nome e preço vem do banco, nunca do cliente.
    /// Channel deve ser um canal manual: PhoneCall | InPerson | WhatsApp | Other.
    /// </summary>
    public async Task<DeliveryOrderDto> CreateManualAsync(
        CreateManualOrderRequest request, CancellationToken ct = default)
    {
        var channel = Enum.Parse<DeliveryChannel>(request.Channel, ignoreCase: true);
        if (channel is DeliveryChannel.Portal
                    or DeliveryChannel.IFood
                    or DeliveryChannel.Rappi
                    or DeliveryChannel.Anotaai)
            throw new DomainException(
                $"Canal '{channel}' não é permitido para pedidos manuais. " +
                "Use PhoneCall, InPerson, WhatsApp ou Other.");

        var orderType = Enum.Parse<DeliveryOrderType>(request.OrderType, ignoreCase: true);

        // Operator is trusted; Takeaway is always 0 by business rule.
        var deliveryFee = orderType == DeliveryOrderType.Takeaway ? 0m : request.DeliveryFee;

        var order = RestDeliveryOrder.Create(
            tenantId:            _currentTenant.Id,
            orderNumber:         0,
            channel:             channel,
            orderType:           orderType,
            customerName:        request.CustomerName,
            customerPhone:       request.CustomerPhone,
            customerEmail:       request.CustomerEmail,
            customerId:          request.CustomerId,
            deliveryAddressJson: request.DeliveryAddressJson,
            deliveryFee:         deliveryFee,
            estimatedMinutes:    request.EstimatedMinutes,
            notes:               request.Notes);

        await SaveOrderWithRetryAsync(order, _repo.GetNextOrderNumberAsync, ct);

        foreach (var itemReq in request.Items ?? [])
        {
            var product = await _products.GetByIdAsync(itemReq.ProductId, ct)
                ?? throw new NotFoundException("Product", itemReq.ProductId);

            if (!product.IsActive)
                throw new DomainException($"Produto '{product.Name}' está inativo.");

            var item = order.AddItem(
                _currentTenant.Id,
                product.Name,
                product.SalePrice,
                itemReq.Quantity,
                product.Id,
                notes: itemReq.Notes);
            _repo.TrackItem(item);

            foreach (var modReq in itemReq.Modifiers ?? [])
            {
                var modifier = await _modifierGroups.GetModifierByIdAsync(modReq.ModifierId, ct)
                    ?? throw new NotFoundException("Modifier", modReq.ModifierId);

                if (!modifier.IsActive)
                    throw new DomainException($"Modificador '{modifier.Name}' está inativo.");

                var snap = item.AddModifier(
                    _currentTenant.Id,
                    modifier.Name,
                    modifier.PriceAdjustment,
                    modifier.Id);
                _repo.TrackModifier(snap);
            }
        }

        await _repo.SaveChangesAsync(ct);
        _logger.LogInformation(
            "DeliveryOrder manual #{Number} criado pelo operador {UserId} via {Channel} (Id: {Id})",
            order.OrderNumber, _currentUser.UserId, channel, order.Id);
        return Map(order);
    }

    // ── CreateFromPortal (cliente público — sem auth, store por PublicSlug) ───

    /// <summary>
    /// Cria pedido via portal público do restaurante.
    /// Store resolvida por PublicSlug. Não há contexto JWT — tenant/store passados
    /// explicitamente para o domínio e o EF interceptor é ignorado para o StoreId.
    /// Produtos validados como IsMenuVisible=true; preços vêm do catálogo.
    /// </summary>
    public async Task<DeliveryOrderDto> CreateFromPortalAsync(
        CreatePortalOrderRequest request, CancellationToken ct = default)
    {
        var store = await _stores.GetByPublicSlugAsync(request.PublicSlug, ct)
            ?? throw new NotFoundException("Store", request.PublicSlug);

        if (store.PublicSlug is null)
            throw new DomainException("O portal deste restaurante não está ativo.");

        var foodSettings = await _settings.GetByStoreIdAsync(store.Id, store.TenantId, ct);
        if (foodSettings is not null && !foodSettings.AcceptingOrders)
            throw new DomainException("O restaurante não está aceitando pedidos no momento.");

        var orderType = Enum.Parse<DeliveryOrderType>(request.OrderType, ignoreCase: true);

        if (foodSettings is not null && orderType == DeliveryOrderType.Delivery && !foodSettings.DeliveryEnabled)
            throw new DomainException("Entrega não está disponível no momento.");
        if (foodSettings is not null && orderType == DeliveryOrderType.Takeaway && !foodSettings.TakeawayEnabled)
            throw new DomainException("Retirada não está disponível no momento.");

        // Delivery fee is never trusted from the client.
        // Takeaway is always 0. Delivery is also 0 for now — no per-store fee configuration yet.
        var deliveryFee = 0m;
        if (orderType == DeliveryOrderType.Delivery)
            _logger.LogWarning(
                "Portal: taxa de entrega definida como 0 — configuração por loja não implementada (Slug: {Slug}). " +
                "Implementar FoodServiceSettings.DeliveryFee quando disponível.",
                store.PublicSlug);

        var order = RestDeliveryOrder.Create(
            tenantId:            store.TenantId,
            orderNumber:         0,
            channel:             DeliveryChannel.Portal,
            orderType:           orderType,
            customerName:        request.CustomerName,
            customerPhone:       request.CustomerPhone,
            customerEmail:       request.CustomerEmail,
            deliveryAddressJson: request.DeliveryAddressJson,
            deliveryFee:         deliveryFee,
            estimatedMinutes:    request.EstimatedMinutes,
            notes:               request.Notes,
            storeId:             store.Id);   // explicit — interceptor não auto-injeta em contexto público

        Task<int> numberFactory(CancellationToken c)
            => _repo.GetNextOrderNumberForStoreAsync(store.TenantId, store.Id, c);

        await SaveOrderWithRetryAsync(order, numberFactory, ct);

        foreach (var itemReq in request.Items ?? [])
        {
            var product = await _products.GetActiveMenuItemAsync(itemReq.ProductId, store.Id, ct)
                ?? throw new NotFoundException("Product", itemReq.ProductId);

            // Load all modifier groups for this product (bypasses query filters — portal has no tenant context)
            var groups = await _modifierGroups.GetByProductIdAsync(product.Id, store.TenantId, ct);

            // Validate: required groups filled, maxSelections respected, modifiers belong to product and are active
            ValidatePortalItemModifiers(product.Id, itemReq.Modifiers ?? [], groups);

            var item = order.AddItem(
                store.TenantId,
                product.Name,
                product.SalePrice,
                itemReq.Quantity,
                product.Id,
                notes: itemReq.Notes);
            _repo.TrackItem(item);

            // Use in-memory lookup — modifiers already validated above, no extra DB calls needed
            var modifierById = groups
                .SelectMany(g => g.Modifiers)
                .ToDictionary(m => m.Id);

            foreach (var modReq in itemReq.Modifiers ?? [])
            {
                var modifier = modifierById[modReq.ModifierId];
                var snap = item.AddModifier(
                    store.TenantId,
                    modifier.Name,
                    modifier.PriceAdjustment,
                    modifier.Id);
                _repo.TrackModifier(snap);
            }
        }

        await _repo.SaveChangesAsync(ct);
        _logger.LogInformation(
            "DeliveryOrder #{Number} criado via Portal '{Slug}' (TenantId: {TenantId}, StoreId: {StoreId}, Id: {Id})",
            order.OrderNumber, store.PublicSlug, store.TenantId, store.Id, order.Id);
        return Map(order);
    }

    // ── Accept ────────────────────────────────────────────────────────────────

    /// <summary>
    /// CRÍTICO: única via de criação de RestOrder a partir de um DeliveryOrder.
    /// Só aceita pedidos em status Received.
    /// WaiterId = operador logado que aceitou.
    /// Todos os itens com ProductId são copiados com snapshot de preço.
    /// Itens sem ProductId (externos não mapeados) são ignorados e logados.
    /// </summary>
    public async Task<DeliveryOrderDto> AcceptAsync(
        Guid id, AcceptDeliveryOrderRequest request, CancellationToken ct = default)
    {
        var deliveryOrder = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("DeliveryOrder", id);

        if (deliveryOrder.Status != DeliveryOrderStatus.Received)
            throw new DomainException(
                $"Só é possível aceitar pedidos com status Received. " +
                $"Status atual: {deliveryOrder.Status}. " +
                $"Use Reject para recusar ou Cancel para cancelar.");

        _logger.LogInformation(
            "AcceptAsync: DeliveryOrder #{Number} (Id: {Id}) sendo aceito pelo operador {UserId}",
            deliveryOrder.OrderNumber, deliveryOrder.Id, _currentUser.UserId);

        // DeliveryOrderType → RestOrderType
        var restOrderType = deliveryOrder.OrderType == DeliveryOrderType.Delivery
            ? RestOrderType.Delivery
            : RestOrderType.Takeaway;

        var orderNumber = await _orders.GetNextNumberAsync(ct);

        var restOrder = RestOrder.Create(
            tenantId:      _currentTenant.Id,
            orderNumber:   orderNumber,
            orderType:     restOrderType,
            tableId:       null,
            partySize:     null,
            waiterId:      _currentUser.UserId,
            couvertAmount: 0,
            customerId:    deliveryOrder.CustomerId,
            notes:         deliveryOrder.Notes);

        await _orders.AddAsync(restOrder, ct);
        await _orders.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AcceptAsync: RestOrder #{RestNumber} (Id: {RestId}, Type: {Type}) criado para DeliveryOrder {DeliveryId}",
            restOrder.OrderNumber, restOrder.Id, restOrderType, deliveryOrder.Id);

        // Copia todos os itens com ProductId para o RestOrder.
        // Itens sem ProductId (canais externos com produtos não mapeados) são ignorados.
        int copied = 0, skipped = 0;

        foreach (var di in deliveryOrder.Items)
        {
            if (di.ProductId is null)
            {
                skipped++;
                continue;
            }

            var item = restOrder.AddItem(
                _currentTenant.Id,
                di.ProductId.Value,
                di.Quantity,
                di.UnitPriceSnapshot,
                di.Notes);
            _orders.TrackItem(item);
            copied++;

            foreach (var dm in di.Modifiers)
            {
                if (dm.ModifierId is null) continue;

                var snap = item.ApplyModifier(
                    _currentTenant.Id,
                    dm.ModifierId.Value,
                    dm.LabelSnapshot,
                    dm.PriceSnapshot);
                _orders.TrackModifier(snap);
            }
        }

        await _orders.SaveChangesAsync(ct);

        if (skipped > 0)
            _logger.LogWarning(
                "AcceptAsync: {Skipped} item(s) ignorados (ProductId nulo) no DeliveryOrder {Id}. " +
                "Esses itens não entram na cozinha. Mapeie os produtos externos para evitar perdas.",
                skipped, deliveryOrder.Id);

        _logger.LogInformation(
            "AcceptAsync: {Copied} item(s) copiados para RestOrder {RestId}",
            copied, restOrder.Id);

        if (request.EstimatedMinutes.HasValue)
            deliveryOrder.SetEstimatedMinutes(request.EstimatedMinutes.Value);

        deliveryOrder.Accept(restOrder.Id);
        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AcceptAsync: DeliveryOrder #{Number} aceito → RestOrder #{RestNumber} (EstimatedMinutes: {Minutes})",
            deliveryOrder.OrderNumber, restOrder.OrderNumber, deliveryOrder.EstimatedMinutes);

        return Map(deliveryOrder);
    }

    // ── Reject ────────────────────────────────────────────────────────────────

    public async Task<DeliveryOrderDto> RejectAsync(
        Guid id, RejectDeliveryOrderRequest request, CancellationToken ct = default)
    {
        var order = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("DeliveryOrder", id);

        order.Reject(request.Reason);
        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DeliveryOrder #{Number} (Id: {Id}) rejeitado. Motivo: {Reason}",
            order.OrderNumber, order.Id, request.Reason ?? "(sem motivo)");

        return Map(order);
    }

    // ── Status update ─────────────────────────────────────────────────────────

    public async Task<DeliveryOrderDto> UpdateStatusAsync(
        Guid id, UpdateDeliveryStatusRequest request, CancellationToken ct = default)
    {
        var order = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("DeliveryOrder", id);

        var status = Enum.Parse<DeliveryOrderStatus>(request.Status, ignoreCase: true);

        switch (status)
        {
            case DeliveryOrderStatus.OutForDelivery:
                if (order.OrderType != DeliveryOrderType.Delivery)
                    throw new DomainException(
                        $"OutForDelivery é exclusivo de pedidos de entrega (tipo atual: {order.OrderType}).");
                if (request.RiderName is not null)
                    order.AssignRider(request.RiderName, request.RiderPhone);
                order.SetOutForDelivery();
                _logger.LogInformation(
                    "DeliveryOrder #{Number} (Id: {Id}) saiu para entrega. Entregador: {Rider}",
                    order.OrderNumber, order.Id, order.RiderName ?? "(não atribuído)");
                break;

            case DeliveryOrderStatus.Delivered:
                order.SetDelivered();
                _logger.LogInformation(
                    "DeliveryOrder #{Number} (Id: {Id}) entregue/retirado com sucesso",
                    order.OrderNumber, order.Id);
                break;

            default:
                throw new DomainException(
                    $"Status '{request.Status}' não pode ser definido via este endpoint. " +
                    "Valores aceitos: OutForDelivery, Delivered.");
        }

        await _repo.SaveChangesAsync(ct);
        return Map(order);
    }

    // ── Assign rider ──────────────────────────────────────────────────────────

    public async Task<DeliveryOrderDto> AssignRiderAsync(
        Guid id, AssignRiderRequest request, CancellationToken ct = default)
    {
        var order = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("DeliveryOrder", id);

        order.AssignRider(request.Name, request.Phone);
        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Entregador '{Rider}' atribuído ao DeliveryOrder #{Number} (Id: {Id})",
            request.Name, order.OrderNumber, order.Id);

        return Map(order);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    public async Task<DeliveryOrderDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("DeliveryOrder", id);

        order.Cancel();
        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DeliveryOrder #{Number} (Id: {Id}) cancelado (Status anterior: {Status})",
            order.OrderNumber, order.Id, order.Status);

        return Map(order);
    }

    // ── IDeliveryOrderSyncService ─────────────────────────────────────────────

    /// <summary>
    /// Sincronização unidirecional chamada pelo OrderService.
    /// No-op se não existir DeliveryOrder vinculado ao RestOrder.
    /// </summary>
    public async Task SyncFromRestOrderAsync(
        Guid restOrderId, RestOrderStatus newStatus, CancellationToken ct = default)
    {
        var order = await _repo.GetByRestOrderIdAsync(restOrderId, ct);
        if (order is null) return;

        var prevStatus = order.Status;

        switch (newStatus)
        {
            case RestOrderStatus.InPreparation:
                if (order.Status == DeliveryOrderStatus.Accepted)
                    order.SetInPreparation();
                break;
            case RestOrderStatus.Ready:
                if (order.Status is DeliveryOrderStatus.Accepted or DeliveryOrderStatus.InPreparation)
                    order.SetReadyForPickup();
                break;
            case RestOrderStatus.Cancelled:
                if (order.Status is not (DeliveryOrderStatus.Delivered
                    or DeliveryOrderStatus.Rejected
                    or DeliveryOrderStatus.Cancelled))
                    order.Cancel();
                break;
        }

        if (order.Status != prevStatus)
            _logger.LogInformation(
                "Sync: DeliveryOrder #{Number} (Id: {Id}) {From} → {To} (RestOrder: {RestId})",
                order.OrderNumber, order.Id, prevStatus, order.Status, restOrderId);

        await _repo.SaveChangesAsync(ct);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Persiste o pedido com retry automático em caso de colisão no OrderNumber.
    /// O número é gerado via <paramref name="numberFactory"/> antes de cada tentativa.
    /// </summary>
    private async Task SaveOrderWithRetryAsync(
        RestDeliveryOrder order,
        Func<CancellationToken, Task<int>> numberFactory,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            order.SetOrderNumber(await numberFactory(ct));
            await _repo.AddAsync(order, ct);
            try
            {
                await _repo.SaveChangesAsync(ct);
                return;
            }
            catch (OrderNumberCollisionException) when (attempt < maxAttempts - 1)
            {
                _repo.Detach(order);
                _logger.LogWarning(
                    "OrderNumber collision ao criar DeliveryOrder — tentativa {Attempt}/{Max}",
                    attempt + 1, maxAttempts);
            }
        }
    }

    // ── Portal modifier validation ────────────────────────────────────────────

    /// <summary>
    /// Validates modifier selections for a portal order item against the product's group rules.
    /// Checks: modifier belongs to product, modifier is active, required groups have >= min selection,
    /// no group exceeds MaxSelections.
    /// All groups for the product must be loaded with their Modifiers included.
    /// </summary>
    private static void ValidatePortalItemModifiers(
        Guid productId,
        List<CreatePortalOrderItemModifierRequest> requested,
        IReadOnlyList<ProductModifierGroup> groups)
    {
        // modifier id → (modifier entity, its group)
        var modifierLookup = groups
            .SelectMany(g => g.Modifiers.Select(m => (m, g)))
            .ToDictionary(x => x.m.Id, x => x);

        foreach (var req in requested)
        {
            if (!modifierLookup.TryGetValue(req.ModifierId, out var entry))
                throw new DomainException(
                    $"Modificador {req.ModifierId} não pertence ao produto {productId} ou não existe.");

            if (!entry.m.IsActive)
                throw new DomainException(
                    $"Modificador '{entry.m.Name}' não está disponível no momento.");
        }

        var countByGroup = requested
            .GroupBy(r => modifierLookup[r.ModifierId].g.Id)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var group in groups)
        {
            var count = countByGroup.GetValueOrDefault(group.Id, 0);
            var min   = group.IsRequired ? Math.Max(1, (int)group.MinSelections) : (int)group.MinSelections;

            if (count < min)
                throw new DomainException(
                    $"Grupo '{group.Name}' requer no mínimo {min} opção(ões). Selecionadas: {count}.");

            if (count > group.MaxSelections)
                throw new DomainException(
                    $"Grupo '{group.Name}' permite no máximo {group.MaxSelections} opção(ões). Selecionadas: {count}.");
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static DeliveryOrderDto Map(RestDeliveryOrder o) => new(
        Id:                  o.Id,
        OrderNumber:         o.OrderNumber,
        TrackingToken:       o.TrackingToken,
        Channel:             o.Channel.ToString(),
        OrderType:           o.OrderType.ToString(),
        Status:              o.Status.ToString(),
        RejectionReason:     o.RejectionReason,
        CustomerName:        o.CustomerName,
        CustomerPhone:       o.CustomerPhone,
        CustomerEmail:       o.CustomerEmail,
        CustomerId:          o.CustomerId,
        DeliveryAddressJson: o.DeliveryAddressJson,
        DeliveryFee:         o.DeliveryFee,
        ItemsSubtotal:       o.ItemsSubtotal,
        Total:               o.Total,
        EstimatedMinutes:    o.EstimatedMinutes,
        RiderName:           o.RiderName,
        RiderPhone:          o.RiderPhone,
        RestOrderId:         o.RestOrderId,
        ExternalOrderId:     o.ExternalOrderId,
        Notes:               o.Notes,
        ReceivedAt:          o.ReceivedAt,
        AcceptedAt:          o.AcceptedAt,
        ReadyAt:             o.ReadyAt,
        DispatchedAt:        o.DispatchedAt,
        DeliveredAt:         o.DeliveredAt,
        CancelledAt:         o.CancelledAt,
        Items:               o.Items.Select(MapItem).ToList());

    private static DeliveryOrderItemDto MapItem(RestDeliveryOrderItem i) => new(
        Id:          i.Id,
        ProductId:   i.ProductId,
        ProductName: i.ProductNameSnapshot,
        UnitPrice:   i.UnitPriceSnapshot,
        Quantity:    i.Quantity,
        LineTotal:   i.LineTotal,
        Notes:       i.Notes,
        Modifiers:   i.Modifiers.Select(m => new DeliveryOrderItemModifierDto(
            m.ModifierId, m.LabelSnapshot, m.PriceSnapshot)).ToList());

    private static DeliveryOrderTrackingDto MapTracking(RestDeliveryOrder o) => new(
        OrderNumber:      o.OrderNumber,
        Status:           o.Status.ToString(),
        StatusLabel:      StatusLabel(o.Status, o.OrderType),
        EstimatedMinutes: o.EstimatedMinutes,
        OrderType:        o.OrderType.ToString());

    private static string StatusLabel(DeliveryOrderStatus status, DeliveryOrderType orderType) =>
        status switch
        {
            DeliveryOrderStatus.Received       => "Pedido recebido",
            DeliveryOrderStatus.Accepted       => "Pedido confirmado",
            DeliveryOrderStatus.InPreparation  => "Preparando seu pedido",
            DeliveryOrderStatus.ReadyForPickup => orderType == DeliveryOrderType.Takeaway
                                                    ? "Pronto para retirada"
                                                    : "Pronto, aguardando entregador",
            DeliveryOrderStatus.OutForDelivery => "Saiu para entrega",
            DeliveryOrderStatus.Delivered      => orderType == DeliveryOrderType.Takeaway
                                                    ? "Retirado com sucesso"
                                                    : "Entregue com sucesso",
            DeliveryOrderStatus.Rejected       => "Pedido não aceito",
            DeliveryOrderStatus.Cancelled      => "Pedido cancelado",
            _                                  => status.ToString(),
        };
}
