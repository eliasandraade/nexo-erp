using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcCatalogItem aggregate.
///   - All access goes through ISvcCatalogItemRepository (tenant/store filtered by EF global query).
///   - TenantId comes from ICurrentTenant; StoreId is auto-injected on INSERT by the interceptor.
/// </summary>
public class SvcCatalogItemService
{
    private readonly ISvcCatalogItemRepository _repo;
    private readonly ICurrentTenant            _currentTenant;

    public SvcCatalogItemService(ISvcCatalogItemRepository repo, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcCatalogItemDto>> GetAllAsync(
        bool onlyActive = false, CancellationToken ct = default)
        => (await _repo.GetAllAsync(onlyActive, ct)).Select(MapToDto).ToList();

    public async Task<SvcCatalogItemDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("SvcCatalogItem", id));

    public async Task<SvcCatalogItemDto> CreateAsync(
        CreateSvcCatalogItemRequest request, CancellationToken ct = default)
    {
        var item = SvcCatalogItem.Create(
            tenantId:          _currentTenant.Id,
            name:              request.Name,
            durationMinutes:   request.DurationMinutes,
            price:             request.Price,
            description:       request.Description,
            category:          request.Category,
            commissionPercent: request.CommissionPercent,
            requiresSubject:   request.RequiresSubject);

        await _repo.AddAsync(item, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(item);
    }

    public async Task<SvcCatalogItemDto> UpdateAsync(
        Guid id, UpdateSvcCatalogItemRequest request, CancellationToken ct = default)
    {
        var item = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("SvcCatalogItem", id);

        item.UpdateDetails(
            request.Name, request.Description, request.Category,
            request.DurationMinutes, request.RequiresSubject);
        item.UpdatePrice(request.Price);
        item.UpdateCommission(request.CommissionPercent);

        _repo.Update(item);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(item);
    }

    public async Task<SvcCatalogItemDto> ActivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: true, ct);

    public async Task<SvcCatalogItemDto> DeactivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: false, ct);

    private async Task<SvcCatalogItemDto> ToggleAsync(Guid id, bool activate, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("SvcCatalogItem", id);

        if (activate) item.Activate();
        else          item.Deactivate();

        _repo.Update(item);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(item);
    }

    internal static SvcCatalogItemDto MapToDto(SvcCatalogItem c) => new(
        Id:                c.Id,
        StoreId:           c.StoreId,
        Name:              c.Name,
        Description:       c.Description,
        Category:          c.Category,
        DurationMinutes:   c.DurationMinutes,
        Price:             c.Price,
        CommissionPercent: c.CommissionPercent,
        RequiresSubject:   c.RequiresSubject,
        IsActive:          c.IsActive,
        CreatedAt:         c.CreatedAt,
        UpdatedAt:         c.UpdatedAt);
}
