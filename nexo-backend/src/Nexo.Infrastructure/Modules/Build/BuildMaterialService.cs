using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Materials of an obra (tenant-scoped, Management): request → quote → order → deliveries (partial/total) or
/// cancel, with delivery history and the late flag. Money stays in the single ledger: this service never creates
/// a <see cref="FinancialMovement"/>; it only links an existing movement of the same obra.
/// </summary>
public sealed class BuildMaterialService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;

    public BuildMaterialService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<BuildMaterialRequestDto>> ListAsync(Guid projectId, string? status, Guid? stageId,
        bool? late, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var q = _db.BldMaterialRequests.AsNoTracking().Where(m => m.TenantId == _tenant.Id && m.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BuildMaterialRequestStatus>(status, true, out var st) || !Enum.IsDefined(st))
                throw new DomainException($"Status inválido: '{status}'.", "invalid_status");
            q = q.Where(m => m.Status == st);
        }
        if (stageId is { } sid) q = q.Where(m => m.StageId == sid);
        if (late == true)
        {
            var today = BusinessClock.Today();
            q = q.Where(m => m.NeededBy != null && m.NeededBy < today
                          && m.Status != BuildMaterialRequestStatus.Delivered && m.Status != BuildMaterialRequestStatus.Cancelled);
        }
        var list = await q.OrderBy(m => m.NeededBy == null).ThenBy(m => m.NeededBy).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        return await MapManyAsync(list, ct);
    }

    public async Task<BuildMaterialRequestDto> GetAsync(Guid id, CancellationToken ct)
        => (await MapManyAsync([await LoadAsync(id, ct, tracking: false)], ct))[0];

    public async Task<BuildMaterialRequestDto> CreateAsync(Guid projectId, SaveBuildMaterialRequest r, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        await EnsureStageAsync(projectId, r.StageId, ct);
        var m = BuildMaterialRequest.Create(_tenant.Id, projectId, r.StageId, r.MaterialName, r.Unit, r.Quantity, r.NeededBy,
            r.Notes, _audit.UserId, _audit.UserName);
        _db.BldMaterialRequests.Add(m);
        _audit.Record(AuditActions.BuildMaterialSaved, "Material", m.Id, $"Material solicitado: {m.MaterialName}.",
            new { projectId, m.Quantity, m.Unit });
        await _db.SaveBuildAsync(ct);
        return await GetAsync(m.Id, ct);
    }

    public async Task<BuildMaterialRequestDto> UpdateAsync(Guid id, SaveBuildMaterialRequest r, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await EnsureStageAsync(m.ProjectId, r.StageId, ct);
        m.Update(r.StageId, r.MaterialName, r.Unit, r.Quantity, r.NeededBy, r.Notes);
        _audit.Record(AuditActions.BuildMaterialSaved, "Material", m.Id, $"Material alterado: {m.MaterialName}.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BuildMaterialRequestDto> QuoteAsync(Guid id, QuoteBuildMaterialRequest r, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await EnsureSupplierAsync(r.SupplierId, ct);
        m.Quote(r.SupplierId, r.EstimatedUnitCost, DateTime.UtcNow);
        return await StatusChangedAsync(m, "cotado", ct);
    }

    public async Task<BuildMaterialRequestDto> OrderAsync(Guid id, OrderBuildMaterialRequest r, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await EnsureSupplierAsync(r.SupplierId, ct);
        m.PlaceOrder(r.SupplierId, r.UnitCost, DateTime.UtcNow);
        return await StatusChangedAsync(m, "pedido", ct);
    }

    public async Task<BuildMaterialRequestDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        m.Cancel(DateTime.UtcNow);
        return await StatusChangedAsync(m, "cancelado", ct);
    }

    /// <summary>Registers a delivery (partial or the rest); the history keeps each one.</summary>
    public async Task<BuildMaterialRequestDto> DeliverAsync(Guid id, DeliverBuildMaterialRequest r, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        var date = r.Date ?? BusinessClock.Today();
        if (date > BusinessClock.Today().AddDays(1))
            throw new DomainException("A data da entrega não pode estar no futuro.", "delivery_in_future");
        m.RegisterDelivery(r.Quantity, date);
        var delivery = BuildMaterialDelivery.Record(_tenant.Id, m.Id, m.ProjectId, r.Quantity, date, r.Note, r.ReceivedBy,
            _audit.UserId, _audit.UserName);
        _db.BldMaterialDeliveries.Add(delivery);
        _audit.Record(AuditActions.BuildMaterialDelivered, "Material", m.Id,
            $"Entrega de {delivery.Quantity:0.###} {m.Unit} de {m.MaterialName} ({(m.Status == BuildMaterialRequestStatus.Delivered ? "total" : "parcial")}).",
            new { delivery.Quantity, date, m.DeliveredQuantity, status = m.Status.ToString() });
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Links (or unlinks with null) the financial movement that paid the material. The movement must be of the
    /// same tenant and of this obra (ContextType Obra, ContextId = project) — otherwise 422 movement_not_of_project.
    /// </summary>
    public async Task<BuildMaterialRequestDto> LinkMovementAsync(Guid id, Guid? movementId, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        if (movementId is { } mid && mid != Guid.Empty)
        {
            var movement = await _db.IntMovements.AsNoTracking()
                .Where(x => x.TenantId == _tenant.Id && x.Id == mid)
                .Select(x => new { x.ContextType, x.ContextId })
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Lançamento não encontrado.");
            if (movement.ContextType != FinancialContextType.Obra || movement.ContextId != m.ProjectId)
                throw new DomainException("Este lançamento não é desta obra.", "movement_not_of_project");
        }
        var before = m.MovementId;
        m.LinkMovement(movementId);
        _audit.Record(AuditActions.BuildMaterialMovementLinked, "Material", m.Id,
            m.MovementId is null ? $"Lançamento desligado do material {m.MaterialName}." : $"Lançamento ligado ao material {m.MaterialName}.",
            new { before, after = m.MovementId });
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BuildMaterialRequestDto> StatusChangedAsync(BuildMaterialRequest m, string what, CancellationToken ct)
    {
        _audit.Record(AuditActions.BuildMaterialStatusChanged, "Material", m.Id, $"Material {m.MaterialName} {what}.",
            new { status = m.Status.ToString(), m.SupplierId, m.EstimatedUnitCost });
        await _db.SaveBuildAsync(ct);
        return await GetAsync(m.Id, ct);
    }

    private async Task EnsureSupplierAsync(Guid? supplierId, CancellationToken ct)
    {
        if (supplierId is { } sid && !await _db.Suppliers.AnyAsync(s => s.TenantId == _tenant.Id && s.Id == sid, ct))
            throw new DomainException("Fornecedor não encontrado.", "supplier_not_found");
    }

    private async Task<BuildMaterialRequest> LoadAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldMaterialRequests.Where(m => m.TenantId == _tenant.Id && m.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Material não encontrado.");
    }

    private async Task EnsureProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
    }

    private async Task EnsureStageAsync(Guid projectId, Guid? stageId, CancellationToken ct)
    {
        if (stageId is { } sid
            && !await _db.BldStages.AnyAsync(s => s.TenantId == _tenant.Id && s.Id == sid && s.ProjectId == projectId, ct))
            throw new DomainException("A etapa informada não pertence a esta obra.", "stage_not_of_project");
    }

    private async Task<IReadOnlyList<BuildMaterialRequestDto>> MapManyAsync(IReadOnlyList<BuildMaterialRequest> list, CancellationToken ct)
    {
        var ids = list.Select(m => m.Id).ToList();
        var stageIds = list.Where(m => m.StageId.HasValue).Select(m => m.StageId!.Value).Distinct().ToList();
        var supplierIds = list.Where(m => m.SupplierId.HasValue).Select(m => m.SupplierId!.Value).Distinct().ToList();
        var deliveries = (await _db.BldMaterialDeliveries.AsNoTracking()
                .Where(d => d.TenantId == _tenant.Id && ids.Contains(d.MaterialRequestId)).ToListAsync(ct))
            .ToLookup(d => d.MaterialRequestId);
        var stages = await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var suppliers = await _db.Suppliers.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.TradeName ?? s.Name, ct);
        var today = BusinessClock.Today();
        return list.Select(m => new BuildMaterialRequestDto(
            m.Id, m.ProjectId, m.StageId, m.StageId is { } sid ? stages.GetValueOrDefault(sid) : null, m.MaterialName, m.Unit,
            m.Quantity, m.NeededBy, m.Status.ToString(), m.IsLate(today), m.SupplierId,
            m.SupplierId is { } sup ? suppliers.GetValueOrDefault(sup) : null, m.EstimatedUnitCost,
            m.EstimatedUnitCost is { } c ? BuildMoney.Round(c * m.Quantity) : null,
            BuildBudgetCalculator.Utc(m.QuotedAt), BuildBudgetCalculator.Utc(m.OrderedAt), m.DeliveredQuantity,
            m.RemainingQuantity, m.DeliveredAt, m.MovementId, m.Notes, m.CreatedByName,
            deliveries[m.Id].OrderBy(d => d.Date).ThenBy(d => d.CreatedAt).Select(d => new BuildMaterialDeliveryDto(
                d.Id, d.Quantity, d.Date, d.Note, d.ReceivedBy, d.RecordedByName, d.CreatedAt)).ToList(),
            m.CreatedAt, m.UpdatedAt)).ToList();
    }
}
