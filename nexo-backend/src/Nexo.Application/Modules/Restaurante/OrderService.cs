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
    private readonly IOrderRepository      _orders;
    private readonly ITableRepository      _tables;
    private readonly IRecipeCardRepository _recipes;
    private readonly IProductRepository    _products;
    private readonly IStockRepository      _stock;
    private readonly SaleService           _saleService;
    private readonly IUnitOfWork           _uow;
    private readonly ICurrentTenant        _currentTenant;
    private readonly ICurrentUser          _currentUser;

    public OrderService(
        IOrderRepository      orders,
        ITableRepository      tables,
        IRecipeCardRepository recipes,
        IProductRepository    products,
        IStockRepository      stock,
        SaleService           saleService,
        IUnitOfWork           uow,
        ICurrentTenant        currentTenant,
        ICurrentUser          currentUser)
    {
        _orders        = orders;
        _tables        = tables;
        _recipes       = recipes;
        _products      = products;
        _stock         = stock;
        _saleService   = saleService;
        _uow           = uow;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
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

    // ── Open ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre uma comanda para a mesa.
    /// Usa SELECT FOR UPDATE para evitar dupla abertura concorrente.
    /// </summary>
    public async Task<OrderDto> OpenAsync(OpenOrderRequest request, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            // Row-level lock na mesa para serializar abertura de comandas concorrentes
            var table = await _tables.GetByIdForUpdateAsync(request.TableId, ct)
                ?? throw new NotFoundException("Table", request.TableId);

            if (!table.IsActive)
                throw new DomainException("Table is inactive.");

            // Verifica se já existe comanda aberta para esta mesa (segunda guarda)
            var existing = await _orders.GetOpenOrderForTableAsync(request.TableId, ct);
            if (existing is not null)
                throw new ConflictException($"Table '{table.Number}' already has an open order (#{existing.OrderNumber}).");

            var number = await _orders.GetNextNumberAsync(ct);

            var order = RestOrder.Create(
                _currentTenant.Id, number,
                request.TableId, _currentUser.UserId,
                request.CustomerId, request.Notes);

            table.SetOccupied();  // automático

            await _orders.AddAsync(order, ct);
            await _orders.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Map(order);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    public async Task<OrderDto> AddItemAsync(Guid orderId, AddOrderItemRequest request, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        var product = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        if (!product.IsActive)
            throw new DomainException($"Product '{product.Name}' is inactive.");

        // snapshot do preço de venda atual
        var item = order.AddItem(_currentTenant.Id, product.Id, request.Quantity, product.SalePrice, request.Notes);

        _orders.TrackItem(item);
        await _orders.SaveChangesAsync(ct);
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

        // ── Idempotência: guarda 1 ────────────────────────────────────────────
        // Paid = already processed; return 409 so the client knows it's idempotent.
        if (order.Status == RestOrderStatus.Paid)
            throw new ConflictException("This order has already been paid.");

        if (order.Status != RestOrderStatus.Closed)
            throw new DomainException(
                order.Status == RestOrderStatus.Cancelled
                    ? "Order is cancelled."
                    : $"Order must be Closed before payment (current: {order.Status}). Call /close first.");

        if (order.SaleId is null)
            throw new DomainException("Order has no linked Sale. This is an inconsistent state — contact support.");

        // ── Idempotência: guarda 2 ────────────────────────────────────────────
        var saleDto = await _saleService.GetByIdAsync(order.SaleId.Value, ct);
        if (saleDto.Status == "Paid")
            throw new ConflictException("This order has already been paid.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            // Products with active recipe cards are excluded from direct stock deduction
            // in ConfirmAsync — ingredient stocks are deducted below via recipe cards.
            var recipeProductIds = new HashSet<Guid>();
            foreach (var item in order.ActiveItems)
            {
                var recipe = await _recipes.GetByProductIdAsync(item.ProductId, ct);
                if (recipe is not null && recipe.IsActive)
                    recipeProductIds.Add(item.ProductId);
            }

            // Confirma a venda no CORE (gera CashMovement / FinancialTransaction)
            var payments = request.Payments
                .Select(p => new PaymentInput(p.Method, p.Type, p.Amount, p.DueDate))
                .ToList();

            await _saleService.ConfirmAsync(order.SaleId.Value,
                new ConfirmSaleRequest(payments, SkipStockProductIds: recipeProductIds.Count > 0 ? recipeProductIds : null), ct);

            // Baixa de ingredientes por ficha técnica
            foreach (var item in order.ActiveItems)
            {
                var recipe = await _recipes.GetByProductIdWithIngredientsAsync(item.ProductId, ct);
                if (recipe is null || !recipe.IsActive) continue;

                foreach (var ingredient in recipe.Ingredients)
                {
                    var ingProduct = await _products.GetByIdAsync(ingredient.IngredientProductId, ct);
                    if (ingProduct is null || !ingProduct.TrackStock) continue;

                    var stockItem = await _stock.GetByProductIdAsync(ingredient.IngredientProductId, ct);
                    if (stockItem is null) continue;

                    // consumo real = (qty vendida / rendimento da ficha) × qty do ingrediente
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
                        costPriceSnapshot: ingProduct.CostPrice);  // snapshot para CMV histórico

                    await _stock.AddMovementAsync(movement, ct);
                }
            }

            // Libera a mesa automaticamente e marca comanda como Paid
            var table = await _tables.GetByIdAsync(order.TableId, ct);
            table?.SetAvailable();
            order.MarkPaid();

            await _orders.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Map(order);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    public async Task<OrderDto> CancelAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
            ?? throw new NotFoundException("Order", orderId);

        order.Cancel();  // throws if Closed or already Cancelled

        var table = await _tables.GetByIdAsync(order.TableId, ct);
        table?.SetAvailable();

        await _orders.SaveChangesAsync(ct);
        return Map(order);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static OrderDto Map(RestOrder o) => new(
        Id:          o.Id,
        OrderNumber: o.OrderNumber,
        Status:      o.Status.ToString(),
        TableId:     o.TableId,
        TableNumber: o.Table?.Number ?? string.Empty,
        WaiterId:    o.WaiterId,
        CustomerId:  o.CustomerId,
        SaleId:      o.SaleId,
        Subtotal:    o.Subtotal,
        Notes:       o.Notes,
        OpenedAt:    o.OpenedAt,
        ClosedAt:    o.ClosedAt,
        CancelledAt: o.CancelledAt,
        Items:       o.Items.Select(MapItem).ToList());

    private static OrderItemDto MapItem(RestOrderItem i) => new(
        Id:              i.Id,
        ProductId:       i.ProductId,
        ProductName:     i.Product?.Name ?? string.Empty,
        Quantity:        i.Quantity,
        UnitPrice:       i.UnitPrice,
        Total:           i.Total,
        Status:          i.Status.ToString(),
        Notes:           i.Notes,
        SentToKitchenAt: i.SentToKitchenAt,
        PreparedAt:      i.PreparedAt,
        DeliveredAt:     i.DeliveredAt,
        CancelledAt:     i.CancelledAt);
}
