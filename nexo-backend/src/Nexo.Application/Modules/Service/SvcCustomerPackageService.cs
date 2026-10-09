using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for assigning packages to customers and consuming balances. Assignment snapshots the
/// package price + item quantities into consumable balances and computes ExpiresAt. Consumption
/// decrements a balance, writes an append-only <see cref="SvcPackageUsage"/>, and auto-marks the
/// package Consumed when every balance reaches zero. Consume may reference an order/order-item for
/// operational history — it NEVER changes the order's total or status. Expired/terminal packages
/// cannot be consumed.
///
/// Each consumption snapshots who performed it, its prorated value and the commission rate, and
/// recognises the commission (SvcCommissionService) in the same transaction.
/// </summary>
public class SvcCustomerPackageService
{
    private readonly ISvcCustomerPackageRepository     _customerPackages;
    private readonly ISvcCustomerPackageItemRepository _customerPackageItems;
    private readonly ISvcPackageUsageRepository        _usages;
    private readonly ISvcPackageRepository             _packages;
    private readonly ICustomerRepository               _customers;
    private readonly ISvcSubjectRepository             _subjects;
    private readonly ISvcOrderRepository               _orders;
    private readonly ISvcOrderItemRepository           _orderItems;
    private readonly ICurrentTenant                    _currentTenant;
    private readonly ISvcProfessionalRepository        _professionals;
    private readonly ISvcCatalogItemRepository         _catalog;
    private readonly ISvcAppointmentRepository         _appointments;
    private readonly SvcCommissionService              _commissions;
    private readonly IUnitOfWork                       _uow;

    public SvcCustomerPackageService(
        ISvcCustomerPackageRepository customerPackages, ISvcCustomerPackageItemRepository customerPackageItems,
        ISvcPackageUsageRepository usages, ISvcPackageRepository packages, ICustomerRepository customers,
        ISvcSubjectRepository subjects, ISvcOrderRepository orders, ISvcOrderItemRepository orderItems,
        ICurrentTenant currentTenant, ISvcProfessionalRepository professionals, ISvcCatalogItemRepository catalog,
        ISvcAppointmentRepository appointments, SvcCommissionService commissions, IUnitOfWork uow)
    {
        _customerPackages = customerPackages; _customerPackageItems = customerPackageItems; _usages = usages;
        _packages = packages; _customers = customers; _subjects = subjects; _orders = orders;
        _orderItems = orderItems; _currentTenant = currentTenant; _professionals = professionals;
        _catalog = catalog; _appointments = appointments; _commissions = commissions; _uow = uow;
    }

    public async Task<IReadOnlyList<SvcCustomerPackageDto>> GetAllAsync(
        Guid? customerId, Guid? subjectId, SvcCustomerPackageStatus? status, Guid? packageId, CancellationToken ct = default)
    {
        var list = await _customerPackages.GetAllAsync(customerId, subjectId, status, packageId, ct);
        var dtos = new List<SvcCustomerPackageDto>(list.Count);
        foreach (var cp in list)
            dtos.Add(MapToDto(cp, await _customerPackageItems.GetByCustomerPackageAsync(cp.Id, ct), usages: []));
        return dtos;
    }

    public async Task<SvcCustomerPackageDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cp = await _customerPackages.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcCustomerPackage", id);
        return MapToDto(cp, cp.Items, await _usages.GetByCustomerPackageAsync(id, ct));
    }

    public async Task<IReadOnlyList<SvcPackageUsageDto>> GetUsagesAsync(Guid id, CancellationToken ct = default)
    {
        _ = await _customerPackages.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcCustomerPackage", id);
        return (await _usages.GetByCustomerPackageAsync(id, ct)).Select(MapUsageToDto).ToList();
    }

    public async Task<SvcCustomerPackageDto> AssignAsync(AssignSvcCustomerPackageRequest r, CancellationToken ct = default)
    {
        var package = await _packages.GetByIdWithItemsAsync(r.PackageId, ct)
            ?? throw new NotFoundException("SvcPackage", r.PackageId);
        if (!package.IsActive)        throw new DomainException("Package is not active.");
        if (package.Items.Count == 0) throw new DomainException("Package has no items to assign.");

        _ = await _customers.GetByIdAsync(r.CustomerId, ct) ?? throw new NotFoundException(nameof(Customer), r.CustomerId);
        if (r.SubjectId is { } sid)
        {
            var subject = await _subjects.GetByIdAsync(sid, ct) ?? throw new NotFoundException("SvcSubject", sid);
            if (subject.CustomerId != r.CustomerId) throw new DomainException("Subject does not belong to the customer.");
        }

        var expiresAt = package.ValidityDays is { } days ? r.StartsAt.AddDays(days) : (DateTime?)null;
        var cp = SvcCustomerPackage.Create(
            _currentTenant.Id, GenerateCode(), package.Id, r.CustomerId, r.SubjectId,
            r.StartsAt, expiresAt, package.Price, r.Notes);
        await _customerPackages.AddAsync(cp, ct);

        var balances = package.Items.Select(pi => SvcCustomerPackageItem.Create(
            _currentTenant.Id, cp.Id, pi.CatalogItemId, pi.NameSnapshot, pi.IncludedQuantity)).ToList();
        foreach (var b in balances) await _customerPackageItems.AddAsync(b, ct);

        // cp + balances are tracked as Added → INSERTed by SaveChanges. Do NOT call Update on them.
        await _customerPackages.SaveChangesAsync(ct);
        return MapToDto(cp, balances, usages: []);
    }

    public async Task<SvcCustomerPackageDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var cp = await _customerPackages.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcCustomerPackage", id);
        cp.Cancel();
        _customerPackages.Update(cp);
        await _customerPackages.SaveChangesAsync(ct);
        return MapToDto(cp, cp.Items, await _usages.GetByCustomerPackageAsync(id, ct));
    }

    public async Task<SvcCustomerPackageDto> ConsumeAsync(Guid id, ConsumeSvcPackageRequest r, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        var cp = await _customerPackages.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcCustomerPackage", id);
        if (cp.Status != SvcCustomerPackageStatus.Active) throw new DomainException($"Cannot consume from a {cp.Status} package.");
        if (cp.IsExpiredAt(DateTime.UtcNow))              throw new DomainException("Package has expired.");

        var balance = cp.Items.FirstOrDefault(i => i.CatalogItemId == r.CatalogItemId)
            ?? throw new NotFoundException("Package balance for catalog item", r.CatalogItemId);

        var appointment = await ValidateAppointmentLinkAsync(r.AppointmentId, r.CatalogItemId, cp, ct);
        var (orderId, orderItemId) = await NormalizeOrderLinkAsync(r.OrderId, r.OrderItemId, appointment, r.CatalogItemId, ct);
        // Same lock a payment takes: the "already commissioned?" checks on both sides see each other.
        if (orderId is { } lockedOrderId) await _orders.LockAsync(lockedOrderId, _currentTenant.Id, ct);
        var linkedProfessionalId = await ValidateOrderLinkAsync(orderId, orderItemId, cp, ct) ?? appointment?.ProfessionalId;
        var professional = r.ProfessionalId is { } explicitId
            ? await ResolveConsumingProfessionalAsync(explicitId, ct)
            : linkedProfessionalId is { } linkedId ? await _professionals.GetByIdAsync(linkedId, ct) : null;

        balance.Consume(r.Quantity);                       // 422 if insufficient / non-positive
        _customerPackageItems.Update(balance);

        // Commission snapshots: prorated value of the consumed units (over everything the package
        // includes) and the rate in force now — catalog item first, then the professional default.
        var baseAmount = SvcCommissionPolicy.ProratePackageValue(
            cp.PriceSnapshot, cp.Items.Sum(i => i.TotalQuantity), r.Quantity);
        decimal? percent = null;
        if (professional is not null)
        {
            var catalog = await _catalog.GetByIdAsync(r.CatalogItemId, ct);
            percent = SvcCommissionPolicy.ResolvePercent(catalog?.CommissionPercent, professional.DefaultCommissionPercent);
        }

        var usage = SvcPackageUsage.Create(
            _currentTenant.Id, cp.Id, balance.Id, r.CatalogItemId, r.Quantity, orderId, orderItemId, r.Notes,
            professional?.Id, baseAmount, percent, appointment?.Id);
        await _usages.AddAsync(usage, ct);

        if (cp.Items.All(i => i.RemainingQuantity == 0m))
        {
            cp.MarkConsumed();
            _customerPackages.Update(cp);
        }

        await _customerPackages.SaveChangesAsync(ct);
        await _commissions.RecognizePackageUsageAsync(usage, cp.CustomerId, $"{cp.Code} · {balance.NameSnapshot}", ct);
        await tx.CommitAsync(ct);
        return MapToDto(cp, cp.Items, await _usages.GetByCustomerPackageAsync(id, ct));
    }

    /// <summary>
    /// An explicitly chosen professional must exist in this tenant/store (404) and be active (422).
    /// One inherited from the linked order is taken as recorded there.
    /// </summary>
    private async Task<SvcProfessional> ResolveConsumingProfessionalAsync(Guid pid, CancellationToken ct)
    {
        var professional = await _professionals.GetByIdAsync(pid, ct) ?? throw new NotFoundException("SvcProfessional", pid);
        if (!professional.IsActive) throw new DomainException("Professional is not active.");
        return professional;
    }

    /// <summary>
    /// The appointment a consumption pays for must exist in this tenant/store (404), belong to the
    /// package's customer, be for the consumed service and not be cancelled / no-show (422).
    /// </summary>
    private async Task<SvcAppointment?> ValidateAppointmentLinkAsync(
        Guid? appointmentId, Guid catalogItemId, SvcCustomerPackage cp, CancellationToken ct)
    {
        if (appointmentId is not { } aid) return null;
        var appointment = await _appointments.GetByIdAsync(aid, ct) ?? throw new NotFoundException("SvcAppointment", aid);
        if (appointment.CustomerId != cp.CustomerId)
            throw new DomainException("Appointment belongs to a different customer than the package.");
        if (appointment.CatalogItemId != catalogItemId)
            throw new DomainException("Appointment is for a different service than the one consumed.");
        if (appointment.Status is SvcAppointmentStatus.Cancelled or SvcAppointmentStatus.NoShow)
            throw new DomainException($"Cannot consume a package for a {appointment.Status} appointment.");
        // One appointment, one service, paid once — a second consumption would earn commission twice.
        if (await _usages.ExistsForAppointmentAsync(aid, ct))
            throw new DomainException("This appointment was already paid with a package.");
        return appointment;
    }

    /// <summary>
    /// Makes the order link precise, so the commission guards can see exactly which order item the
    /// package paid for: an appointment that already has an order links that order, and an order
    /// link without an item points at the order's first item for the consumed service.
    /// </summary>
    private async Task<(Guid? OrderId, Guid? OrderItemId)> NormalizeOrderLinkAsync(
        Guid? orderId, Guid? orderItemId, SvcAppointment? appointment, Guid catalogItemId, CancellationToken ct)
    {
        if (orderId is null && appointment is not null)
            orderId = (await _orders.GetAllAsync(null, null, null, null, appointment.Id, ct)).FirstOrDefault()?.Id;
        if (orderId is { } oid && orderItemId is null)
            orderItemId = (await _orderItems.GetByOrderAsync(oid, ct))
                .Where(i => i.CatalogItemId == catalogItemId)
                .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
                .FirstOrDefault()?.Id;
        return (orderId, orderItemId);
    }

    /// <summary>Validates the optional order link and returns the professional it implies (item first, then order).</summary>
    private async Task<Guid?> ValidateOrderLinkAsync(Guid? orderId, Guid? orderItemId, SvcCustomerPackage cp, CancellationToken ct)
    {
        if (orderId is not { } oid) return null;           // orderItemId-without-orderId rejected by the validator (400)
        var order = await _orders.GetByIdAsync(oid, ct) ?? throw new NotFoundException("SvcOrder", oid);
        if (order.CustomerId != cp.CustomerId)
            throw new DomainException("Order belongs to a different customer than the package.");
        if (cp.SubjectId is { } sid && order.SubjectId != sid)
            throw new DomainException("Order subject does not match the package subject.");
        if (orderItemId is not { } oiid) return order.ProfessionalId;

        var item = await _orderItems.GetByIdAsync(oiid, ct) ?? throw new NotFoundException("SvcOrderItem", oiid);
        if (item.OrderId != oid) throw new DomainException("Order item does not belong to the order.");
        return item.ProfessionalId ?? order.ProfessionalId;
    }

    private static string GenerateCode() => $"PKG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..19].ToUpperInvariant();

    internal static SvcCustomerPackageDto MapToDto(
        SvcCustomerPackage cp, IEnumerable<SvcCustomerPackageItem> items, IEnumerable<SvcPackageUsage> usages) => new(
        Id: cp.Id, StoreId: cp.StoreId, Code: cp.Code, PackageId: cp.PackageId, CustomerId: cp.CustomerId,
        SubjectId: cp.SubjectId, Status: cp.Status, StartsAt: cp.StartsAt, ExpiresAt: cp.ExpiresAt,
        PriceSnapshot: cp.PriceSnapshot, Notes: cp.Notes,
        Items: items.Select(MapItemToDto).ToList(), Usages: usages.Select(MapUsageToDto).ToList(),
        CreatedAt: cp.CreatedAt, UpdatedAt: cp.UpdatedAt);

    private static SvcCustomerPackageItemDto MapItemToDto(SvcCustomerPackageItem i) => new(
        Id: i.Id, CustomerPackageId: i.CustomerPackageId, CatalogItemId: i.CatalogItemId, NameSnapshot: i.NameSnapshot,
        TotalQuantity: i.TotalQuantity, RemainingQuantity: i.RemainingQuantity, CreatedAt: i.CreatedAt, UpdatedAt: i.UpdatedAt);

    private static SvcPackageUsageDto MapUsageToDto(SvcPackageUsage u) => new(
        Id: u.Id, CustomerPackageId: u.CustomerPackageId, CustomerPackageItemId: u.CustomerPackageItemId,
        CatalogItemId: u.CatalogItemId, OrderId: u.OrderId, OrderItemId: u.OrderItemId, Quantity: u.Quantity,
        Notes: u.Notes, ProfessionalId: u.ProfessionalId, AppointmentId: u.AppointmentId, BaseAmountSnapshot: u.BaseAmountSnapshot,
        CommissionPercentSnapshot: u.CommissionPercentSnapshot, CreatedAt: u.CreatedAt);
}
