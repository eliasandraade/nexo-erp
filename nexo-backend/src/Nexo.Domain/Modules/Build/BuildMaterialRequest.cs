using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

public enum BuildMaterialRequestStatus
{
    Requested          = 1,
    Quoted             = 2,
    Ordered            = 3,
    Delivered          = 4,
    PartiallyDelivered = 5,
    Cancelled          = 6,
}

/// <summary>
/// A material needed on site: requested → quoted → ordered → (partially) delivered, or cancelled. Deliveries are
/// kept as history (<see cref="BuildMaterialDelivery"/>). Paying it is a <c>FinancialMovement</c> of the obra,
/// created in the financial module and only linked here — this entity never creates money records.
/// </summary>
public class BuildMaterialRequest : TenantEntity
{
    private BuildMaterialRequest() { }
    private BuildMaterialRequest(Guid tenantId) : base(tenantId) { }

    public Guid                       ProjectId         { get; private set; }
    public Guid?                      StageId           { get; private set; }
    public string                     MaterialName      { get; private set; } = string.Empty;
    public string                     Unit              { get; private set; } = string.Empty;
    public decimal                    Quantity          { get; private set; }
    public DateOnly?                  NeededBy          { get; private set; }
    public BuildMaterialRequestStatus Status            { get; private set; }
    public Guid?                      SupplierId        { get; private set; }
    public decimal?                   EstimatedUnitCost { get; private set; }
    public DateTime?                  QuotedAt          { get; private set; }
    public DateTime?                  OrderedAt         { get; private set; }
    public decimal                    DeliveredQuantity { get; private set; }
    public DateOnly?                  DeliveredAt       { get; private set; }
    public DateTime?                  CancelledAt       { get; private set; }
    public Guid?                      MovementId        { get; private set; }
    public string?                    Notes             { get; private set; }
    public Guid                       CreatedBy         { get; private set; }
    public string                     CreatedByName     { get; private set; } = string.Empty;

    public static BuildMaterialRequest Create(Guid tenantId, Guid projectId, Guid? stageId, string materialName,
        string unit, decimal quantity, DateOnly? neededBy, string? notes, Guid createdBy, string? createdByName)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        var m = new BuildMaterialRequest(tenantId)
        {
            ProjectId     = projectId,
            Status        = BuildMaterialRequestStatus.Requested,
            CreatedBy     = createdBy,
            CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? "usuário" : createdByName.Trim(),
        };
        m.Apply(stageId, materialName, unit, quantity, neededBy, notes);
        return m;
    }

    public void Update(Guid? stageId, string materialName, string unit, decimal quantity, DateOnly? neededBy, string? notes)
    {
        if (Status is not (BuildMaterialRequestStatus.Requested or BuildMaterialRequestStatus.Quoted))
            throw new DomainException("Depois do pedido de compra o material não pode mais ser alterado.", "material_not_editable");
        Apply(stageId, materialName, unit, quantity, neededBy, notes);
        SetUpdatedAt();
    }

    private void Apply(Guid? stageId, string materialName, string unit, decimal quantity, DateOnly? neededBy, string? notes)
    {
        if (quantity <= 0m) throw new DomainException("A quantidade precisa ser maior que zero.");
        StageId      = stageId;
        MaterialName = BuildMoney.Required(materialName, 300, "O material");
        Unit         = BuildMoney.Required(unit, 20, "A unidade");
        Quantity     = BuildMoney.RoundQuantity(quantity);
        NeededBy     = neededBy;
        Notes        = BuildMoney.Clean(notes, 2000, "As observações");
    }

    /// <summary>Requested | Quoted → Quoted (a new quote replaces the previous one).</summary>
    public void Quote(Guid? supplierId, decimal? estimatedUnitCost, DateTime nowUtc)
    {
        if (Status is not (BuildMaterialRequestStatus.Requested or BuildMaterialRequestStatus.Quoted))
            throw Transition("cotar");
        SetCost(estimatedUnitCost);
        SupplierId = supplierId ?? SupplierId;
        QuotedAt   = nowUtc;
        Status     = BuildMaterialRequestStatus.Quoted;
        SetUpdatedAt();
    }

    /// <summary>Requested | Quoted → Ordered.</summary>
    public void PlaceOrder(Guid? supplierId, decimal? unitCost, DateTime nowUtc)
    {
        if (Status is not (BuildMaterialRequestStatus.Requested or BuildMaterialRequestStatus.Quoted))
            throw Transition("pedir");
        if (unitCost is not null) SetCost(unitCost);
        SupplierId = supplierId ?? SupplierId;
        OrderedAt  = nowUtc;
        Status     = BuildMaterialRequestStatus.Ordered;
        SetUpdatedAt();
    }

    /// <summary>A delivery of an ordered material: partial until the delivered quantity reaches the requested one.</summary>
    public void RegisterDelivery(decimal quantity, DateOnly date)
    {
        if (Status is not (BuildMaterialRequestStatus.Ordered or BuildMaterialRequestStatus.PartiallyDelivered))
            throw new DomainException("Só materiais pedidos (e ainda não entregues por completo) recebem entrega.",
                "material_not_ordered");
        if (quantity <= 0m) throw new DomainException("A quantidade entregue precisa ser maior que zero.");
        var total = BuildMoney.RoundQuantity(DeliveredQuantity + quantity);
        if (total > Quantity)
            throw new DomainException($"A entrega passa da quantidade pedida (faltam {Quantity - DeliveredQuantity:0.###} {Unit}).",
                "delivery_exceeds_quantity");
        DeliveredQuantity = total;
        DeliveredAt       = DeliveredAt is { } last && last > date ? last : date;
        Status            = total >= Quantity ? BuildMaterialRequestStatus.Delivered : BuildMaterialRequestStatus.PartiallyDelivered;
        SetUpdatedAt();
    }

    public void Cancel(DateTime nowUtc)
    {
        if (Status is BuildMaterialRequestStatus.Delivered or BuildMaterialRequestStatus.Cancelled)
            throw Transition("cancelar");
        Status      = BuildMaterialRequestStatus.Cancelled;
        CancelledAt = nowUtc;
        SetUpdatedAt();
    }

    /// <summary>Links (or unlinks, with null) the obra's financial movement that paid this material.</summary>
    public void LinkMovement(Guid? movementId)
    {
        MovementId = movementId is { } id && id != Guid.Empty ? id : null;
        SetUpdatedAt();
    }

    public decimal RemainingQuantity => Math.Max(0m, Quantity - DeliveredQuantity);

    /// <summary>Needed before today and not delivered (nor cancelled).</summary>
    public bool IsLate(DateOnly today)
        => NeededBy is { } n && n < today
           && Status is not (BuildMaterialRequestStatus.Delivered or BuildMaterialRequestStatus.Cancelled);

    private void SetCost(decimal? unitCost)
    {
        if (unitCost is < 0m) throw new DomainException("O custo unitário não pode ser negativo.");
        EstimatedUnitCost = unitCost is { } c ? BuildMoney.Round(c) : EstimatedUnitCost;
    }

    private DomainException Transition(string action)
        => new($"Não é possível {action} um material com status {Status}.", "material_invalid_transition");
}

/// <summary>One delivery of a material request (history; never edited).</summary>
public class BuildMaterialDelivery : TenantEntity
{
    private BuildMaterialDelivery() { }
    private BuildMaterialDelivery(Guid tenantId) : base(tenantId) { }

    public Guid     MaterialRequestId { get; private set; }
    public Guid     ProjectId         { get; private set; }
    public decimal  Quantity          { get; private set; }
    public DateOnly Date              { get; private set; }
    public string?  Note              { get; private set; }
    public string?  ReceivedBy        { get; private set; }
    public Guid     RecordedBy        { get; private set; }
    public string   RecordedByName    { get; private set; } = string.Empty;

    public static BuildMaterialDelivery Record(Guid tenantId, Guid materialRequestId, Guid projectId, decimal quantity,
        DateOnly date, string? note, string? receivedBy, Guid recordedBy, string? recordedByName)
    {
        if (materialRequestId == Guid.Empty || projectId == Guid.Empty) throw new DomainException("Material inválido.");
        if (quantity <= 0m) throw new DomainException("A quantidade entregue precisa ser maior que zero.");
        return new BuildMaterialDelivery(tenantId)
        {
            MaterialRequestId = materialRequestId,
            ProjectId         = projectId,
            Quantity          = BuildMoney.RoundQuantity(quantity),
            Date              = date,
            Note              = BuildMoney.Clean(note, 1000, "A observação"),
            ReceivedBy        = BuildMoney.Clean(receivedBy, 200, "Quem recebeu"),
            RecordedBy        = recordedBy,
            RecordedByName    = string.IsNullOrWhiteSpace(recordedByName) ? "usuário" : recordedByName.Trim(),
        };
    }
}
