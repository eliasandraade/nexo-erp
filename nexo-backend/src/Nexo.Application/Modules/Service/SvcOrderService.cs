using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcOrder aggregate. Orders are created manually or from an appointment.
/// All references (customer/subject/professional/catalog) are validated through tenant/store-
/// filtered repositories (cross-tenant → 404); inactive professional/catalog and subject/customer
/// mismatch and RequiresSubject → 422. TotalAmount is always recomputed from items server-side.
/// </summary>
public class SvcOrderService
{
    private readonly ISvcOrderRepository        _orders;
    private readonly ISvcOrderItemRepository    _items;
    private readonly ICustomerRepository        _customers;
    private readonly ISvcSubjectRepository      _subjects;
    private readonly ISvcProfessionalRepository _professionals;
    private readonly ISvcCatalogItemRepository  _catalog;
    private readonly ISvcAppointmentRepository  _appointments;
    private readonly ICurrentTenant             _currentTenant;

    public SvcOrderService(
        ISvcOrderRepository orders, ISvcOrderItemRepository items, ICustomerRepository customers,
        ISvcSubjectRepository subjects, ISvcProfessionalRepository professionals,
        ISvcCatalogItemRepository catalog, ISvcAppointmentRepository appointments,
        ICurrentTenant currentTenant)
    {
        _orders = orders; _items = items; _customers = customers; _subjects = subjects;
        _professionals = professionals; _catalog = catalog; _appointments = appointments;
        _currentTenant = currentTenant;
    }

    // ── Queries ──────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<SvcOrderDto>> GetAllAsync(
        SvcOrderStatus? status, Guid? customerId, Guid? subjectId, Guid? professionalId,
        Guid? appointmentId, CancellationToken ct = default)
    {
        var orders = await _orders.GetAllAsync(status, customerId, subjectId, professionalId, appointmentId, ct);
        var dtos = new List<SvcOrderDto>(orders.Count);
        foreach (var o in orders)
            dtos.Add(MapToDto(o, await _items.GetByOrderAsync(o.Id, ct)));
        return dtos;
    }

    public async Task<SvcOrderDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
        return MapToDto(order, order.Items);
    }

    // ── Create (manual) ──────────────────────────────────────────────────────
    public async Task<SvcOrderDto> CreateAsync(CreateSvcOrderRequest r, CancellationToken ct = default)
    {
        await EnsureCustomerAsync(r.CustomerId, ct);
        await EnsureSubjectAsync(r.SubjectId, r.CustomerId, ct);
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);

        var order = SvcOrder.Create(
            _currentTenant.Id, GenerateCode(), r.CustomerId, r.SubjectId, r.ProfessionalId, null, r.Notes);
        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, []);
    }

    // ── Create (from appointment) ────────────────────────────────────────────
    public async Task<SvcOrderDto> CreateFromAppointmentAsync(Guid appointmentId, CancellationToken ct = default)
    {
        var appt = await _appointments.GetByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException("SvcAppointment", appointmentId);

        if (appt.Status is SvcAppointmentStatus.Cancelled or SvcAppointmentStatus.NoShow)
            throw new DomainException($"Cannot create an order from a {appt.Status} appointment.");

        if (await _orders.ExistsForAppointmentAsync(appointmentId, ct))
            throw new ConflictException("An order already exists for this appointment.");

        var catalog = await _catalog.GetByIdAsync(appt.CatalogItemId, ct)
            ?? throw new NotFoundException("SvcCatalogItem", appt.CatalogItemId);

        var order = SvcOrder.Create(
            _currentTenant.Id, GenerateCode(), appt.CustomerId,
            appt.SubjectId, appt.ProfessionalId, appt.Id, null);
        await _orders.AddAsync(order, ct);

        // Initial item: price snapshot from the APPOINTMENT, name/commission from the catalog.
        var item = SvcOrderItem.Create(
            _currentTenant.Id, order.Id, appt.CatalogItemId, appt.ProfessionalId,
            catalog.Name, catalog.Description, 1m, appt.PriceSnapshot, catalog.CommissionPercent);
        await _items.AddAsync(item, ct);

        // order is tracked as Added; the recalculated total is persisted by the INSERT.
        // (Do NOT call _orders.Update here — that would flip Added→Modified and emit an UPDATE.)
        order.RecalculateTotal(new[] { item });
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, new[] { item });
    }

    // ── Update / status ──────────────────────────────────────────────────────
    public async Task<SvcOrderDto> UpdateAsync(Guid id, UpdateSvcOrderRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
        order.EnsureEditable();

        await EnsureSubjectAsync(r.SubjectId, order.CustomerId, ct);
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);
        if (r.SubjectId is null) await EnsureNoItemRequiresSubjectAsync(order.Id, ct);

        order.UpdateDetails(r.SubjectId, r.ProfessionalId, r.Notes);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, await _items.GetByOrderAsync(order.Id, ct));
    }

    public async Task<SvcOrderDto> ChangeStatusAsync(Guid id, ChangeSvcOrderStatusRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
        order.ChangeStatus(r.Status!.Value, r.Reason);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, await _items.GetByOrderAsync(order.Id, ct));
    }

    // ── Items ────────────────────────────────────────────────────────────────
    public async Task<SvcOrderDto> AddItemAsync(Guid orderId, AddSvcOrderItemRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        order.EnsureEditable();

        var catalog = await _catalog.GetByIdAsync(r.CatalogItemId, ct)
            ?? throw new NotFoundException("SvcCatalogItem", r.CatalogItemId);
        if (!catalog.IsActive) throw new DomainException("Catalog item is not active.");
        if (catalog.RequiresSubject && order.SubjectId is null)
            throw new DomainException("This service requires a subject on the order.");
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);

        var item = SvcOrderItem.Create(
            _currentTenant.Id, orderId, r.CatalogItemId, r.ProfessionalId,
            catalog.Name, catalog.Description, r.Quantity, catalog.Price, catalog.CommissionPercent);
        await _items.AddAsync(item, ct);

        var all = (await _items.GetByOrderAsync(orderId, ct)).Append(item).ToList();
        order.RecalculateTotal(all);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, all);
    }

    public async Task<SvcOrderDto> UpdateItemAsync(
        Guid orderId, Guid itemId, UpdateSvcOrderItemRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        order.EnsureEditable();
        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.OrderId != orderId) throw new NotFoundException("SvcOrderItem", itemId);
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);

        item.Update(r.Quantity, r.ProfessionalId);
        _items.Update(item);

        var all = await _items.GetByOrderAsync(orderId, ct);
        order.RecalculateTotal(all);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, all);
    }

    public async Task<SvcOrderDto> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        order.EnsureEditable();
        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.OrderId != orderId) throw new NotFoundException("SvcOrderItem", itemId);

        _items.Remove(item);
        var remaining = (await _items.GetByOrderAsync(orderId, ct)).Where(i => i.Id != itemId).ToList();
        order.RecalculateTotal(remaining);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, remaining);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string GenerateCode()
        => $"OS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    private async Task EnsureCustomerAsync(Guid customerId, CancellationToken ct)
        => _ = await _customers.GetByIdAsync(customerId, ct) ?? throw new NotFoundException(nameof(Customer), customerId);

    private async Task EnsureSubjectAsync(Guid? subjectId, Guid customerId, CancellationToken ct)
    {
        if (subjectId is not { } sid) return;
        var subject = await _subjects.GetByIdAsync(sid, ct) ?? throw new NotFoundException("SvcSubject", sid);
        if (subject.CustomerId != customerId)
            throw new DomainException("Subject does not belong to the customer.");
    }

    private async Task EnsureProfessionalActiveAsync(Guid? professionalId, CancellationToken ct)
    {
        if (professionalId is not { } pid) return;
        var professional = await _professionals.GetByIdAsync(pid, ct)
            ?? throw new NotFoundException("SvcProfessional", pid);
        if (!professional.IsActive) throw new DomainException("Professional is not active.");
    }

    private async Task EnsureNoItemRequiresSubjectAsync(Guid orderId, CancellationToken ct)
    {
        var items = await _items.GetByOrderAsync(orderId, ct);
        foreach (var it in items)
        {
            var catalog = await _catalog.GetByIdAsync(it.CatalogItemId, ct);
            if (catalog is { RequiresSubject: true })
                throw new DomainException("An item on this order requires a subject; cannot clear it.");
        }
    }

    private static SvcOrderDto MapToDto(SvcOrder o, IEnumerable<SvcOrderItem> items) => new(
        Id: o.Id, StoreId: o.StoreId, Code: o.Code, CustomerId: o.CustomerId, SubjectId: o.SubjectId,
        ProfessionalId: o.ProfessionalId, AppointmentId: o.AppointmentId, Status: o.Status, Notes: o.Notes,
        CancellationReason: o.CancellationReason, TotalAmount: o.TotalAmount,
        Items: items.Select(MapItemToDto).ToList(), CreatedAt: o.CreatedAt, UpdatedAt: o.UpdatedAt);

    private static SvcOrderItemDto MapItemToDto(SvcOrderItem i) => new(
        Id: i.Id, OrderId: i.OrderId, CatalogItemId: i.CatalogItemId, ProfessionalId: i.ProfessionalId,
        NameSnapshot: i.NameSnapshot, DescriptionSnapshot: i.DescriptionSnapshot, Quantity: i.Quantity,
        UnitPriceSnapshot: i.UnitPriceSnapshot, CommissionPercentSnapshot: i.CommissionPercentSnapshot,
        TotalAmount: i.TotalAmount, CreatedAt: i.CreatedAt, UpdatedAt: i.UpdatedAt);
}
