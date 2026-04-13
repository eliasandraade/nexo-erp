using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Mesa do restaurante.
///
/// Transições de status:
///   Available  ──► Occupied    (auto: OrderService.OpenAsync)
///   Available  ──► Reserved    (manual: TableService.UpdateStatusAsync)
///   Available  ──► Maintenance (manual)
///   Occupied   ──► Available   (auto: OrderService.PayAsync / CancelAsync)
///   Reserved   ──► Available   (manual)
///   Maintenance──► Available   (manual)
/// </summary>
public class RestTable : StoreEntity
{
    private RestTable() { }
    private RestTable(Guid tenantId) : base(tenantId) { }

    public Guid           AreaId   { get; private set; }
    public string         Number   { get; private set; } = string.Empty;  // "1", "A3", "Varanda 2"
    public int            Capacity { get; private set; }
    public RestTableStatus Status  { get; private set; }
    public bool           IsActive { get; private set; }

    // Navigation
    public RestArea? Area { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RestTable Create(
        Guid tenantId, Guid areaId, string number, int capacity = 4)
        => new RestTable(tenantId)
        {
            AreaId   = areaId,
            Number   = number.Trim(),
            Capacity = capacity > 0 ? capacity : 4,
            Status   = RestTableStatus.Available,
            IsActive = true,
        };

    // ── Mutation ──────────────────────────────────────────────────────────────

    public void Update(string number, int capacity, Guid areaId)
    {
        Number   = number.Trim();
        Capacity = capacity > 0 ? capacity : Capacity;
        AreaId   = areaId;
        SetUpdatedAt();
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public void SetOccupied()
    {
        if (Status != RestTableStatus.Available)
            throw new DomainException($"Table '{Number}' is not available (current: {Status}).");
        Status = RestTableStatus.Occupied;
        SetUpdatedAt();
    }

    public void SetAvailable()
    {
        Status = RestTableStatus.Available;
        SetUpdatedAt();
    }

    public void SetReserved()
    {
        if (Status != RestTableStatus.Available)
            throw new DomainException($"Only Available tables can be reserved (current: {Status}).");
        Status = RestTableStatus.Reserved;
        SetUpdatedAt();
    }

    public void SetMaintenance()
    {
        if (Status == RestTableStatus.Occupied)
            throw new DomainException("Cannot set Occupied table to Maintenance. Close or cancel the order first.");
        Status = RestTableStatus.Maintenance;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
