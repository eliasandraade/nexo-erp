using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class AreaService
{
    private readonly IAreaRepository _areas;
    private readonly ICurrentTenant  _currentTenant;

    public AreaService(IAreaRepository areas, ICurrentTenant currentTenant)
    {
        _areas         = areas;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<AreaDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var list = await _areas.GetAllAsync(includeInactive, ct);
        return list.Select(Map).ToList();
    }

    public async Task<AreaDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var area = await _areas.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Area", id);
        return Map(area);
    }

    public async Task<AreaDto> CreateAsync(CreateAreaRequest request, CancellationToken ct = default)
    {
        var area = RestArea.Create(_currentTenant.Id, request.Name, request.Description);
        await _areas.AddAsync(area, ct);
        await _areas.SaveChangesAsync(ct);
        return Map(area);
    }

    public async Task<AreaDto> UpdateAsync(Guid id, UpdateAreaRequest request, CancellationToken ct = default)
    {
        var area = await _areas.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Area", id);
        area.Update(request.Name, request.Description);
        if (request.IsActive) area.Activate(); else area.Deactivate();
        await _areas.SaveChangesAsync(ct);
        return Map(area);
    }

    private static AreaDto Map(RestArea a) => new(
        a.Id, a.Name, a.Description, a.IsActive,
        TableCount: a.Tables.Count,
        a.CreatedAt);
}
