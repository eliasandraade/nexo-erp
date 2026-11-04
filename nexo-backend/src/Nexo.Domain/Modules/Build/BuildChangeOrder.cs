using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

public enum BuildChangeOrderReason
{
    ClientRequest = 1,
    DesignChange  = 2,
    SiteCondition = 3,
    Error         = 4,
    Other         = 5,
}

public enum BuildChangeOrderStatus
{
    Draft        = 1,
    SentToClient = 2,
    Approved     = 3,
    Rejected     = 4,
    Cancelled    = 5,
}

/// <summary>Where a decision (change order, selection) was taken.</summary>
public enum BuildDecisionChannel
{
    Internal = 1,
    Portal   = 2,
}

/// <summary>
/// A change order (aditivo) of a project: what changes, why, its cost impact (may be negative) and its
/// schedule impact in days (may be zero or negative) on one stage. Draft → SentToClient → Approved/Rejected
/// (or Cancelled). The client decides through the password portal; the company may decide internally. A
/// decision is final: deciding again is a conflict. Approval effects (contract value, stage end, schedule
/// revision) are applied by the service in the same transaction.
/// </summary>
public class BuildChangeOrder : TenantEntity
{
    public const string PortalDecider = "Cliente (portal)";

    private BuildChangeOrder() { }
    private BuildChangeOrder(Guid tenantId) : base(tenantId) { }

    public Guid                   ProjectId          { get; private set; }
    /// <summary>Sequential per project (#1, #2…) — for people; the portal only uses <see cref="PublicId"/>.</summary>
    public int                    Number             { get; private set; }
    /// <summary>Opaque id used by the client portal.</summary>
    public Guid                   PublicId           { get; private set; }
    public string                 Title              { get; private set; } = string.Empty;
    public string?                Description        { get; private set; }
    public BuildChangeOrderReason Reason             { get; private set; }
    public decimal                CostImpact         { get; private set; }
    public int                    ScheduleImpactDays { get; private set; }
    public Guid?                  StageId            { get; private set; }
    public BuildChangeOrderStatus Status             { get; private set; }
    public DateTime?              SentAt             { get; private set; }
    public DateTime?              DecidedAt          { get; private set; }
    /// <summary>Internal user name, or "Cliente (portal)".</summary>
    public string?                DecidedBy          { get; private set; }
    public Guid?                  DecidedByUserId    { get; private set; }
    public BuildDecisionChannel?  DecisionChannel    { get; private set; }
    public string?                ClientComment      { get; private set; }
    /// <summary>Name typed by the client when deciding through the portal.</summary>
    public string?                SignerName         { get; private set; }
    public Guid?                  SourceSelectionId  { get; private set; }
    /// <summary>Whether the approval was added to the project's approved budget (it had one at the time).</summary>
    public bool                   AppliedToBudget    { get; private set; }
    public Guid                   CreatedBy          { get; private set; }
    public string                 CreatedByName      { get; private set; } = string.Empty;

    public static BuildChangeOrder Create(Guid tenantId, Guid projectId, int number, string title, string? description,
        BuildChangeOrderReason reason, decimal costImpact, int scheduleImpactDays, Guid? stageId, Guid createdBy,
        string? createdByName, Guid? sourceSelectionId = null)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (number < 1) throw new DomainException("Número do aditivo inválido.");
        var co = new BuildChangeOrder(tenantId)
        {
            ProjectId         = projectId,
            Number            = number,
            PublicId          = Guid.NewGuid(),
            Status            = BuildChangeOrderStatus.Draft,
            SourceSelectionId = sourceSelectionId,
            CreatedBy         = createdBy,
            CreatedByName     = string.IsNullOrWhiteSpace(createdByName) ? "usuário" : createdByName.Trim(),
        };
        co.Apply(title, description, reason, costImpact, scheduleImpactDays, stageId);
        return co;
    }

    public void Update(string title, string? description, BuildChangeOrderReason reason, decimal costImpact,
        int scheduleImpactDays, Guid? stageId)
    {
        if (Status != BuildChangeOrderStatus.Draft)
            throw new DomainException("Só aditivos em rascunho podem ser alterados.", "change_order_not_draft");
        Apply(title, description, reason, costImpact, scheduleImpactDays, stageId);
        SetUpdatedAt();
    }

    private void Apply(string title, string? description, BuildChangeOrderReason reason, decimal costImpact,
        int scheduleImpactDays, Guid? stageId)
    {
        if (!Enum.IsDefined(reason)) throw new DomainException("Motivo do aditivo inválido.");
        if (Math.Abs(costImpact) > 1_000_000_000m) throw new DomainException("Valor do aditivo fora do limite.");
        if (scheduleImpactDays is < -3650 or > 3650) throw new DomainException("Impacto no prazo fora do limite (±3650 dias).");
        if (scheduleImpactDays != 0 && stageId is null)
            throw new DomainException("Informe a etapa afetada para um aditivo que muda o prazo.", "change_order_stage_required");
        Title              = BuildMoney.Required(title, 200, "O título do aditivo");
        Description        = BuildMoney.Clean(description, 4000, "A descrição");
        Reason             = reason;
        CostImpact         = BuildMoney.Round(costImpact);
        ScheduleImpactDays = scheduleImpactDays;
        StageId            = stageId;
    }

    public bool IsDecided => Status is BuildChangeOrderStatus.Approved or BuildChangeOrderStatus.Rejected;

    /// <summary>Draft → SentToClient (visible in the portal when the flag is on).</summary>
    public void Send(DateTime nowUtc)
    {
        if (Status != BuildChangeOrderStatus.Draft)
            throw new DomainException("Só aditivos em rascunho podem ser enviados ao cliente.", "change_order_not_draft");
        Status = BuildChangeOrderStatus.SentToClient;
        SentAt = nowUtc;
        SetUpdatedAt();
    }

    public void Approve(BuildDecisionChannel channel, string decidedBy, Guid? decidedByUserId, string? comment,
        string? signerName, DateTime nowUtc)
        => Decide(BuildChangeOrderStatus.Approved, channel, decidedBy, decidedByUserId, comment, signerName, nowUtc);

    public void Reject(BuildDecisionChannel channel, string decidedBy, Guid? decidedByUserId, string? comment,
        string? signerName, DateTime nowUtc)
        => Decide(BuildChangeOrderStatus.Rejected, channel, decidedBy, decidedByUserId, comment, signerName, nowUtc);

    private void Decide(BuildChangeOrderStatus to, BuildDecisionChannel channel, string decidedBy, Guid? userId,
        string? comment, string? signerName, DateTime nowUtc)
    {
        if (IsDecided) throw new ConflictException("Este aditivo já foi decidido.");
        if (Status == BuildChangeOrderStatus.Cancelled)
            throw new DomainException("Este aditivo foi cancelado.", "change_order_cancelled");
        if (!Enum.IsDefined(channel)) throw new DomainException("Canal de decisão inválido.");
        // the client only decides what was sent to it; the company may also decide a draft directly
        if (channel == BuildDecisionChannel.Portal && Status != BuildChangeOrderStatus.SentToClient)
            throw new DomainException("Este aditivo não está aguardando a decisão do cliente.", "change_order_not_sent");
        Status          = to;
        DecidedAt       = nowUtc;
        DecidedBy       = BuildMoney.Required(decidedBy, 200, "Quem decidiu");
        DecidedByUserId = userId;
        DecisionChannel = channel;
        ClientComment   = BuildMoney.Clean(comment, 2000, "O comentário");
        SignerName      = BuildMoney.Clean(signerName, 200, "O nome de quem assina");
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (IsDecided) throw new ConflictException("Este aditivo já foi decidido.");
        if (Status == BuildChangeOrderStatus.Cancelled) return;
        Status = BuildChangeOrderStatus.Cancelled;
        SetUpdatedAt();
    }

    public void MarkAppliedToBudget()
    {
        AppliedToBudget = true;
        SetUpdatedAt();
    }
}

/// <summary>Optional breakdown line of a change order (informative; the cost impact is the order's own field).</summary>
public class BuildChangeOrderItem : TenantEntity
{
    private BuildChangeOrderItem() { }
    private BuildChangeOrderItem(Guid tenantId) : base(tenantId) { }

    public Guid          ChangeOrderId { get; private set; }
    public int           Order         { get; private set; }
    public string        Name          { get; private set; } = string.Empty;
    public BuildCostKind Kind          { get; private set; }
    public decimal       Quantity      { get; private set; }
    public string        Unit          { get; private set; } = string.Empty;
    public decimal       UnitCost      { get; private set; }
    public decimal       TotalCost     { get; private set; }

    public static BuildChangeOrderItem Create(Guid tenantId, Guid changeOrderId, int order, string name,
        BuildCostKind kind, decimal quantity, string unit, decimal unitCost)
    {
        if (changeOrderId == Guid.Empty) throw new DomainException("Aditivo inválido.");
        if (quantity <= 0m) throw new DomainException("A quantidade precisa ser maior que zero.");
        // a negative unit cost is allowed: a credit line (e.g. material removed from the scope)
        if (Math.Abs(unitCost) > 1_000_000_000m) throw new DomainException("Custo unitário fora do limite.");
        return new BuildChangeOrderItem(tenantId)
        {
            ChangeOrderId = changeOrderId,
            Order         = Math.Max(0, order),
            Name          = BuildMoney.Required(name, 300, "O nome do item"),
            Kind          = BuildMoney.Kind(kind),
            Quantity      = BuildMoney.RoundQuantity(quantity),
            Unit          = BuildMoney.Required(unit, 20, "A unidade"),
            UnitCost      = BuildMoney.Round(unitCost),
            TotalCost     = BuildMoney.Round(quantity * unitCost),
        };
    }
}
