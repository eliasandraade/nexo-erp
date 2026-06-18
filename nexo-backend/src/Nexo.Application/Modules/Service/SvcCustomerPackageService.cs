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

    public SvcCustomerPackageService(
        ISvcCustomerPackageRepository customerPackages, ISvcCustomerPackageItemRepository customerPackageItems,
        ISvcPackageUsageRepository usages, ISvcPackageRepository packages, ICustomerRepository customers,
        ISvcSubjectRepository subjects, ISvcOrderRepository orders, ISvcOrderItemRepository orderItems,
        ICurrentTenant currentTenant)
    {
        _customerPackages = customerPackages; _customerPackageItems = customerPackageItems; _usages = usages;
        _packages = packages; _customers = customers; _subjects = subjects; _orders = orders;
        _orderItems = orderItems; _currentTenant = currentTenant;
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
        var cp = await _customerPackages.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcCustomerPackage", id);
        if (cp.Status != SvcCustomerPackageStatus.Active) throw new DomainException($"Cannot consume from a {cp.Status} package.");
        if (cp.IsExpiredAt(DateTime.UtcNow))              throw new DomainException("Package has expired.");

        var balance = cp.Items.FirstOrDefault(i => i.CatalogItemId == r.CatalogItemId)
            ?? throw new NotFoundException("Package balance for catalog item", r.CatalogItemId);

        await ValidateOrderLinkAsync(r.OrderId, r.OrderItemId, cp, ct);

        balance.Consume(r.Quantity);                       // 422 if insufficient / non-positive
        _customerPackageItems.Update(balance);

        var usage = SvcPackageUsage.Create(
            _currentTenant.Id, cp.Id, balance.Id, r.CatalogItemId, r.Quantity, r.OrderId, r.OrderItemId, r.Notes);
        await _usages.AddAsync(usage, ct);

        if (cp.Items.All(i => i.RemainingQuantity == 0m))
        {
            cp.MarkConsumed();
            _customerPackages.Update(cp);
        }

        await _customerPackages.SaveChangesAsync(ct);
        return MapToDto(cp, cp.Items, await _usages.GetByCustomerPackageAsync(id, ct));
    }

    private async Task ValidateOrderLinkAsync(Guid? orderId, Guid? orderItemId, SvcCustomerPackage cp, CancellationToken ct)
    {
        if (orderId is not { } oid) return;                // orderItemId-without-orderId rejected by the validator (400)
        var order = await _orders.GetByIdAsync(oid, ct) ?? throw new NotFoundException("SvcOrder", oid);
        if (order.CustomerId != cp.CustomerId)
            throw new DomainException("Order belongs to a different customer than the package.");
        if (cp.SubjectId is { } sid && order.SubjectId != sid)
            throw new DomainException("Order subject does not match the package subject.");
        if (orderItemId is { } oiid)
        {
            var item = await _orderItems.GetByIdAsync(oiid, ct) ?? throw new NotFoundException("SvcOrderItem", oiid);
            if (item.OrderId != oid) throw new DomainException("Order item does not belong to the order.");
        }
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
        Notes: u.Notes, CreatedAt: u.CreatedAt);
}
