using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class TableService
{
    private readonly ITableRepository _tables;
    private readonly IAreaRepository  _areas;
    private readonly ICurrentTenant   _currentTenant;

    public TableService(
        ITableRepository tables, IAreaRepository areas, ICurrentTenant currentTenant)
    {
        _tables        = tables;
        _areas         = areas;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<TableDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var list = await _tables.GetAllAsync(includeInactive, ct);
        return list.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<TableDto>> GetByAreaAsync(Guid areaId, CancellationToken ct = default)
    {
        var list = await _tables.GetByAreaAsync(areaId, ct);
        return list.Select(Map).ToList();
    }

    public async Task<TableDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var table = await _tables.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Table", id);
        return Map(table);
    }

    public async Task<TableDto> CreateAsync(CreateTableRequest request, CancellationToken ct = default)
    {
        _ = await _areas.GetByIdAsync(request.AreaId, ct)
            ?? throw new NotFoundException("Area", request.AreaId);

        var table = RestTable.Create(_currentTenant.Id, request.AreaId, request.Number, request.Capacity);
        await _tables.AddAsync(table, ct);
        await _tables.SaveChangesAsync(ct);
        return Map(table);
    }

    public async Task<TableDto> UpdateAsync(Guid id, UpdateTableRequest request, CancellationToken ct = default)
    {
        var table = await _tables.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Table", id);

        _ = await _areas.GetByIdAsync(request.AreaId, ct)
            ?? throw new NotFoundException("Area", request.AreaId);

        table.Update(request.Number, request.Capacity, request.AreaId);
        if (request.IsActive) table.Activate(); else table.Deactivate();
        await _tables.SaveChangesAsync(ct);
        return Map(table);
    }

    /// <summary>
    /// Atualiza status manualmente (operador).
    /// Apenas Available, Reserved e Maintenance são permitidos aqui.
    /// Occupied é definido automaticamente pelo OrderService.
    /// </summary>
    public async Task<TableDto> UpdateStatusAsync(Guid id, UpdateTableStatusRequest request, CancellationToken ct = default)
    {
        var table = await _tables.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Table", id);

        var status = Enum.Parse<RestTableStatus>(request.Status, ignoreCase: true);

        switch (status)
        {
            case RestTableStatus.Available:
                table.SetAvailable();
                break;
            case RestTableStatus.Reserved:
                table.SetReserved();
                break;
            case RestTableStatus.Maintenance:
                table.SetMaintenance();
                break;
            case RestTableStatus.Occupied:
                throw new DomainException("'Occupied' is set automatically when an order is opened. Use the orders endpoint.");
            default:
                throw new DomainException($"Unknown status: {request.Status}");
        }

        await _tables.SaveChangesAsync(ct);
        return Map(table);
    }

    private static TableDto Map(RestTable t) => new(
        t.Id, t.AreaId,
        AreaName: t.Area?.Name ?? string.Empty,
        t.Number, t.Capacity, t.Status.ToString(),
        t.IsActive, t.CreatedAt);
}
