using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Sales;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

/// <summary>
/// Gerencia o ciclo de vida das comandas do restaurante.
///
/// Concorrência de mesa:
///   OpenAsync usa GetByIdForUpdateAsync (SELECT FOR UPDATE) dentro de uma transação
///   para garantir que não existam duas comandas abertas para a mesma mesa.
///   O índice UNIQUE parcial na migration é a segunda linha de defesa.
///
/// Idempotência de pagamento:
///   PayAsync valida order.Status == Closed e sale.Status != Paid.
///   Double-submission retorna 409 Conflict.
///
/// Baixa de insumos:
///   Acontece em PayAsync, após SaleService.ConfirmAsync, para cada item
///   que tenha ficha técnica. Usa StockMovementType.RecipeOutput com
///   CostPriceSnapshot do ingrediente para preservar histórico de CMV.
/// </summary>
public class OrderService
{
    private readonly IOrderRepository              _orders;
    private readonly ITableRepository              _tables;
    private readonly IRecipeCardRepository         _recipes;
    private readonly IProductRepository            _products;
    private readonly IStockRepository              _stock;
    private readonly SaleService                   _saleService;
    private readonly IUnitOfWork                   _uow;
    private readonly ICurrentTenant                _currentTenant;
    private readonly ICurrentUser                  _currentUser;
    private readonly IFoodServiceSettingsRepository _foodSettings;
    private readonly IModifierGroupRepository      _modifierGroups;
    private readonly IRestaurantNotificationService _notifications;

    public OrderService(
        IOrderRepository               orders,
        ITableRepository               tables,
        IRecipeCardRepository          recipes,
        IProductRepository             products,
        IStockRepository               stock,
        SaleService                    saleService,
        IUnitOfWork                    uow,
        ICurrentTenant                 currentTenant,
        ICurrentUser                   currentUser,
        IFoodServiceSettingsRepository foodSettings,
        IModifierGroupRepository       modifierGroups,
        IRestaurantNotificationService notifications)
    {
        _orders         = orders;
        _tables         = tables;
        _recipes        = recipes;
        _products       = products;
        _stock          = stock;
        _saleService    = saleService;
        _uow            = uow;
        _currentTenant  = currentTenant;
        _currentUser    = currentUser;
        _foodSettings   = foodSettings;
        _modifierGroups = modifierGroups;
        _notifications  = notifications;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrderDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _orders.GetAllAsync(ct);
        return list.Select(Map).ToList();
    }

    public async Task<OrderDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("Order", id);
        return Map(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetByTableIdAsync(Guid tableId, CancellationToken ct = default)
    {
        var orders = await _orders.GetOrdersByTableIdAsync(tableId, ct);
        return orders.Select(Map).ToList();
    }

    // ── Open ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre uma comanda.
    /// Counter/Takeaway: caminho rápido sem transação.
    /// DineIn: usa SELECT FOR UPDATE para evitar dupla abertura concorrente.
    /// Quando CouvertAutomatic=true, aplica couvert automaticamente via PartySize.
    /// </summary>
    public async Task<OrderDto> OpenAsync(OpenOrderRequest request, CancellationToken ct = default)
    {
        var orderType = Enum.Parse<RestOrderType>(request.OrderType, ignoreCase: true);
        var settings  = await _foodSettings.GetCurrentStoreAsync(ct);

        // Validate PartySize when CouvertAutomatic = true
        if (settings is { CouvertEnabled: true, CouvertAutomatic: true } && request.PartySize is null)
            throw new DomainException("PartySize is required when CouvertAutomatic is enabled.");

        // Counter/Takeaway: no table, no lock
        if (orderType != RestOrderType.DineIn)
        {
            if (request.TableId.HasValue)
                throw new DomainException($"{orderType} orders do not use a table.");

            var number = await _orders.GetNextNumberAsync(ct);
            decimal couvertAmount = 0;
            if (settings is { CouvertEnabled: true, CouvertAutomatic: true } && request.PartySize.HasValue)
                couvertAmount = (settings.CouvertPricePerPerson ?? 0) * request.PartySize.Value;

            var order = RestOrder.Create(
                _currentTenant.Id, number, orderType, null,
                request.PartySize, _currentUser.UserId,
                couvertAmount, request.CustomerId, request.Notes);

            await _orders.AddAsync(order, ct);
            await _orders.SaveChangesAsync(ct);
            return Map(order);
        }

        // DineIn: require table, use SELECT FOR UPDATE
        if (request.TableId is null)
            throw new DomainException("DineIn orders require a TableId.");

        OrderDto? dineInResult = null;
        await _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            var table = await _tables.GetByIdForUpdateAsync(request.TableId.Value, innerCt)
                ?? throw new NotFoundException("Table", request.TableId.Value);

            if (!table.IsActive)
                throw new DomainException("Table is inactive.");

            var existing = await _orders.GetOpenOrderForTableAsync(request.TableId.Value, innerCt);
            if (existing is not null)
                throw new ConflictException($"Table '{table.Number}' already has an open order (#{existing.OrderNumber}).");

            var number = await _orders.GetNextNumberAsync(innerCt);
            decimal couvertAmount = 0;
            if (settings is { CouvertEnabled: true, CouvertAutomatic: true } && request.PartySize.HasValue)
                couvertAmount = (settings.CouvertPricePerPerson ?? 0) * request.PartySize.Value;

            var order = RestOrder.Create(
                _currentTenant.Id, number, orderType, request.TableId,
                request.PartySize, _currentUser.UserId,
                couvertAmount, request.CustomerId, request.Notes);

            table.SetOccupied();
            await _orders.AddAsync(order, innerCt);
            await _orders.SaveChangesAsync(innerCt);
            dineInResult = Map(order);
        }, ct);
        _ = _notifications.TableStatusChangedAsync(request.TableId!.Value, "Occupied");
        return dineInResult!;
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    public async Task<OrderDto> AddItemAsync(
        Guid orderId, AddOrderItemRequest request, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        var product = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        if (!product.IsActive)
            throw new DomainException($"Product '{product.Name}' is inactive.");

        // Validate required modifier groups
        var groups = await _modifierGroups.GetByProductIdAsync(product.Id, ct);
        var requestedModifierIds = (request.Modifiers ?? []).Select(m => m.ModifierId).ToHashSet();

        foreach (var group in groups.Where(g => g.IsRequired))
        {
            var hasSelection = group.Modifiers.Any(m => requestedModifierIds.Contains(m.Id));
            if (!hasSelection)
                throw new DomainException(
                    $"Modifier group '{group.Name}' is required. Select at least one option.");
        }

        var item = order.AddItem(
            _currentTenant.Id, product.Id, request.Quantity, product.SalePrice, request.Notes);
        _orders.TrackItem(item);

        // Apply modifier snapshots
        foreach (var modReq in request.Modifiers ?? [])
        {
            var modifier = await _modifierGroups.GetModifierByIdAsync(modReq.ModifierId, ct)
                ?? throw new NotFoundException("Modifier", modReq.ModifierId);

            if (!modifier.IsActive)
                throw new DomainException($"Modifier '{modifier.Name}' is not active.");

            var snap = item.ApplyModifier(
                _currentTenant.Id, modifier.Id, modifier.Name, modifier.PriceAdjustment);
            _orders.TrackModifier(snap);
        }

        await _orders.SaveChangesAsync(ct);
        // Fire-and-forget: SignalR notification (UX complement; polling fallback is authoritative)
        _ = _notifications.NewItemAddedAsync(order.Id, MapItem(item));
        return Map(order);
    }

    public async Task<OrderDto> UpdateItemStatusAsync(
        Guid orderId, Guid itemId, UpdateOrderItemStatusRequest request, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        var item = order.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundException("OrderItem", itemId);

        var status = Enum.Parse<RestOrderItemStatus>(request.Status, ignoreCase: true);

        switch (status)
        {
            case RestOrderItemStatus.Preparing:
                item.SetPreparing();
                // Se todos os itens ativos estão em Preparing ou além → order vai para InPreparation
                if (order.Status == RestOrderStatus.Open &&
                    order.ActiveItems.All(i => i.Status != RestOrderItemStatus.Pending))
                    order.SetInPreparation();
                break;
            case RestOrderItemStatus.Ready:
                item.SetReady();
                // Se todos os itens ativos estão Ready → order vai para Ready
                if (order.ActiveItems.All(i => i.Status == RestOrderItemStatus.Ready))
                    order.SetReady();
                break;
            case RestOrderItemStatus.Delivered:
                item.SetDelivered();
                break;
            default:
                throw new DomainException($"Status '{request.Status}' cannot be set via this endpoint.");
        }

        await _orders.SaveChangesAsync(ct);
        _ = _notifications.OrderItemStatusChangedAsync(order.Id, itemId, item.Status.ToString());
        _ = _notifications.OrderStatusChangedAsync(order.Id, order.Status.ToString());
        return Map(order);
    }

    public async Task<OrderDto> CancelItemAsync(Guid orderId, Guid itemId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        order.CancelItem(itemId);
        await _orders.SaveChangesAsync(ct);
        return Map(order);
    }

    // ── Close (Etapa 1) ───────────────────────────────────────────────────────

    /// <summary>
    /// Fecha a comanda: gera Sale em Draft no CORE.
    /// A mesa permanece Occupied até o pagamento ser confirmado.
    /// </summary>
    public async Task<CloseOrderResponse> CloseAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        if (!order.IsModifiable)
            throw new DomainException($"Order cannot be closed in status {order.Status}.");

        if (!order.ActiveItems.Any())
            throw new DomainException("Cannot close an order with no active items.");

        // Cria Sale em Draft no CORE
        var saleDto = await _saleService.CreateAsync(new CreateSaleRequest(
            CustomerId:    order.CustomerId,
            CashSessionId: null,
            Notes:         $"Comanda #{order.OrderNumber}"), ct);

        // Adiciona itens ativos ao Sale
        foreach (var item in order.ActiveItems)
        {
            await _saleService.AddItemAsync(saleDto.Id, new AddSaleItemRequest(
                ProductId:      item.ProductId,
                Quantity:       item.Quantity,
                UnitPrice:      item.UnitPrice,
                DiscountAmount: 0,
                Notes:          item.Notes), ct);
        }

        // Vincula Sale à comanda
        order.Close(saleDto.Id);
        await _orders.SaveChangesAsync(ct);

        return new CloseOrderResponse(
            OrderId: order.Id,
            SaleId:  saleDto.Id,
            Total:   order.Subtotal,
            Message: "Order closed. Proceed to /pay to confirm payment.");
    }

    // ── Pay (Etapa 2) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Confirma o pagamento da comanda.
    /// Idempotência: valida order.Status == Closed e sale.Status != Paid.
    /// Após SaleService.ConfirmAsync, baixa os ingredientes das fichas técnicas.
    /// Mesa fica Available após sucesso.
    /// </summary>
    public async Task<OrderDto> PayAsync(Guid orderId, PayOrderRequest request, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        if (order.Status == RestOrderStatus.Paid)
            throw new ConflictException("This order has already been paid.");

        if (order.Status != RestOrderStatus.Closed)
            throw new DomainException(
                order.Status == RestOrderStatus.Cancelled
                    ? "Order is cancelled."
                    : $"Order must be Closed before payment (current: {order.Status}). Call /close first.");

        if (order.SaleId is null)
            throw new DomainException("Order has no linked Sale. Inconsistent state — contact support.");

        var saleDto = await _saleService.GetByIdAsync(order.SaleId.Value, ct);
        if (saleDto.Status == "Paid")
            throw new ConflictException("This order has already been paid.");

        var settings = await _foodSettings.GetCurrentStoreAsync(ct);

        // Manual couvert: when CouvertAutomatic=false and PartySize is provided at payment time,
        // recalculate couvert based on the actual party size. This supports restaurants that only
        // know the party size at the end of the meal (e.g., after splitting tables).
        if (settings is { CouvertEnabled: true, CouvertAutomatic: false } && request.PartySize.HasValue)
        {
            order.SetPartySize(request.PartySize.Value);
            var couvert = (settings.CouvertPricePerPerson ?? 0) * request.PartySize.Value;
            order.SetCouvert(couvert);
        }

        // ─── Payment formula ─────────────────────────────────────────────────────────
        // ItemsSubtotal   = Σ(item.Total) — already includes modifier price adjustments
        // ServiceFeeAmount = ItemsSubtotal × (ServiceFeePercent / 100)
        //                   ↑ service fee is applied ONLY to ItemsSubtotal, never to couvert
        // CouvertAmount   = set at order open (auto) or recalculated here (manual, see above)
        // SurchargesAmount = CouvertAmount + ServiceFeeAmount
        // GrandTotal      = ItemsSubtotal + SurchargesAmount
        // ─────────────────────────────────────────────────────────────────────────────
        decimal serviceFeeAmount = 0;
        if (settings is { ServiceFeeEnabled: true } && settings.ServiceFeePercent.HasValue)
            serviceFeeAmount = Math.Round(order.ItemsSubtotal * (settings.ServiceFeePercent.Value / 100m), 2);

        order.SetServiceFee(serviceFeeAmount);

        decimal surchargesAmount = order.CouvertAmount + serviceFeeAmount;
        decimal grandTotal       = order.ItemsSubtotal + surchargesAmount;
        decimal paymentsTotal    = request.Payments.Sum(p => p.Amount);

        if (paymentsTotal < grandTotal)
            throw new DomainException(
                $"Payment amount ({paymentsTotal:F2}) is less than order total ({grandTotal:F2}).");

        OrderDto? payResult = null;
        await _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            var recipeProductIds = new HashSet<Guid>();
            foreach (var item in order.ActiveItems)
            {
                var recipe = await _recipes.GetByProductIdAsync(item.ProductId, innerCt);
                if (recipe is not null && recipe.IsActive)
                    recipeProductIds.Add(item.ProductId);
            }

            var payments = request.Payments
                .Select(p => new PaymentInput(p.Method, p.Type, p.Amount, p.DueDate))
                .ToList();

            await _saleService.ConfirmAsync(order.SaleId.Value,
                new ConfirmSaleRequest(
                    payments,
                    SurchargesAmount: surchargesAmount > 0 ? surchargesAmount : 0,
                    SkipStockProductIds: recipeProductIds.Count > 0 ? recipeProductIds : null),
                innerCt);

            // Ingredient deduction via recipe cards
            foreach (var item in order.ActiveItems)
            {
                var recipe = await _recipes.GetByProductIdWithIngredientsAsync(item.ProductId, innerCt);
                if (recipe is null || !recipe.IsActive) continue;

                foreach (var ingredient in recipe.Ingredients)
                {
                    var ingProduct = await _products.GetByIdAsync(ingredient.IngredientProductId, innerCt);
                    if (ingProduct is null || !ingProduct.TrackStock) continue;

                    var stockItem = await _stock.GetByProductIdAsync(ingredient.IngredientProductId, innerCt);
                    if (stockItem is null) continue;

                    var consumption = (item.Quantity / recipe.Yield) * ingredient.Quantity;
                    var qtyBefore   = stockItem.CurrentQuantity;
                    stockItem.ApplyMovement(-consumption);

                    var movement = StockMovement.Create(
                        tenantId:          _currentTenant.Id,
                        productId:         ingredient.IngredientProductId,
                        movementType:      StockMovementType.RecipeOutput,
                        quantity:          consumption,
                        quantityBefore:    qtyBefore,
                        quantityAfter:     stockItem.CurrentQuantity,
                        createdByUserId:   _currentUser.UserId,
                        referenceType:     "Order",
                        referenceId:       order.Id,
                        notes:             $"Ficha técnica — Comanda #{order.OrderNumber} — {ingProduct.Name}",
                        costPriceSnapshot: ingProduct.CostPrice);

                    await _stock.AddMovementAsync(movement, innerCt);
                }
            }

            // Release table — only DineIn orders have a table
            if (order.TableId.HasValue)
            {
                var table = await _tables.GetByIdAsync(order.TableId.Value, innerCt);
                table?.SetAvailable();
            }

            order.MarkPaid();
            await _orders.SaveChangesAsync(innerCt);
            payResult = Map(order);
        }, ct);
        if (order.TableId.HasValue)
            _ = _notifications.TableStatusChangedAsync(order.TableId.Value, "Available");
        _ = _notifications.OrderStatusChangedAsync(order.Id, "Paid");
        return payResult!;
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    public async Task<OrderDto> CancelAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        order.Cancel();  // throws if Closed or already Cancelled

        if (order.TableId.HasValue)
        {
            var table = await _tables.GetByIdAsync(order.TableId.Value, ct);
            table?.SetAvailable();
        }

        await _orders.SaveChangesAsync(ct);
        return Map(order);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static OrderDto Map(RestOrder o) => new(
        Id:               o.Id,
        OrderNumber:      o.OrderNumber,
        Status:           o.Status.ToString(),
        OrderType:        o.OrderType.ToString(),
        TableId:          o.TableId,
        TableNumber:      o.Table?.Number,
        PartySize:        o.PartySize,
        WaiterId:         o.WaiterId,
        CustomerId:       o.CustomerId,
        SaleId:           o.SaleId,
        ItemsSubtotal:    o.ItemsSubtotal,
        CouvertAmount:    o.CouvertAmount,
        ServiceFeeAmount: o.ServiceFeeAmount,
        Total:            o.Total,
        Notes:            o.Notes,
        OpenedAt:         o.OpenedAt,
        ClosedAt:         o.ClosedAt,
        CancelledAt:      o.CancelledAt,
        Items:            o.Items.Select(MapItem).ToList());

    private static OrderItemDto MapItem(RestOrderItem i) => new(
        Id:              i.Id,
        ProductId:       i.ProductId,
        ProductName:     i.Product?.Name ?? string.Empty,
        Quantity:        i.Quantity,
        UnitPrice:       i.UnitPrice,
        Total:           i.Total,
        Status:          i.Status.ToString(),
        Notes:           i.Notes,
        Modifiers:       i.Modifiers.Select(m => new OrderItemModifierDto(m.ModifierId, m.LabelSnapshot, m.PriceSnapshot)).ToList(),
        SentToKitchenAt: i.SentToKitchenAt,
        PreparedAt:      i.PreparedAt,
        DeliveredAt:     i.DeliveredAt,
        CancelledAt:     i.CancelledAt);
}
