using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

public enum BuildSelectionStatus
{
    Open      = 1,
    Submitted = 2,
    Confirmed = 3,
    Cancelled = 4,
}

/// <summary>
/// A choice the client has to make (e.g. "Piso da sala") among options priced relative to the standard one.
/// Open → Submitted (client chose, through the portal or recorded by the company) → Confirmed by the company
/// (or Cancelled). Confirming an option that changes price or lead time creates a draft change order — the
/// chain selection → change order → budget/schedule.
/// </summary>
public class BuildSelection : TenantEntity
{
    private BuildSelection() { }
    private BuildSelection(Guid tenantId) : base(tenantId) { }

    public Guid                  ProjectId       { get; private set; }
    /// <summary>Opaque id used by the client portal.</summary>
    public Guid                  PublicId        { get; private set; }
    public string                Title           { get; private set; } = string.Empty;
    public string?               Description     { get; private set; }
    public Guid?                 StageId         { get; private set; }
    /// <summary>Last day the client may choose (inclusive).</summary>
    public DateOnly?             Deadline        { get; private set; }
    public BuildSelectionStatus  Status          { get; private set; }
    /// <summary>Whether the client portal shows this selection (and signs its option images).</summary>
    public bool                  ClientVisible   { get; private set; }
    public Guid?                 ChosenOptionId  { get; private set; }
    public string?               ClientComment   { get; private set; }
    public DateTime?             DecidedAt       { get; private set; }
    public BuildDecisionChannel? ChosenVia       { get; private set; }
    public DateTime?             ConfirmedAt     { get; private set; }
    public string?               ConfirmedByName { get; private set; }
    /// <summary>The draft change order created when the confirmed option changes price or lead time.</summary>
    public Guid?                 ChangeOrderId   { get; private set; }
    public Guid                  CreatedBy       { get; private set; }

    public static BuildSelection Create(Guid tenantId, Guid projectId, string title, string? description, Guid? stageId,
        DateOnly? deadline, bool clientVisible, Guid createdBy)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        var s = new BuildSelection(tenantId)
        {
            ProjectId = projectId,
            PublicId  = Guid.NewGuid(),
            Status    = BuildSelectionStatus.Open,
            CreatedBy = createdBy,
        };
        s.Apply(title, description, stageId, deadline, clientVisible);
        return s;
    }

    public void Update(string title, string? description, Guid? stageId, DateOnly? deadline, bool clientVisible)
    {
        EnsureOpen();
        Apply(title, description, stageId, deadline, clientVisible);
        SetUpdatedAt();
    }

    private void Apply(string title, string? description, Guid? stageId, DateOnly? deadline, bool clientVisible)
    {
        Title         = BuildMoney.Required(title, 200, "O título da escolha");
        Description   = BuildMoney.Clean(description, 4000, "A descrição");
        StageId       = stageId;
        Deadline      = deadline;
        ClientVisible = clientVisible;
    }

    public bool IsDeadlinePassed(DateOnly today) => Deadline is { } d && d < today;

    /// <summary>The client's (or recorded) choice: only while Open and up to the deadline.</summary>
    public void Choose(Guid optionId, string? comment, DateOnly today, DateTime nowUtc, BuildDecisionChannel channel)
    {
        if (Status != BuildSelectionStatus.Open)
            throw new ConflictException("Esta escolha já foi feita ou encerrada.");
        if (IsDeadlinePassed(today))
            throw new DomainException($"O prazo desta escolha terminou em {Deadline:dd/MM/yyyy}.", "selection_deadline_passed");
        if (optionId == Guid.Empty) throw new DomainException("Escolha uma opção.");
        ChosenOptionId = optionId;
        ClientComment  = BuildMoney.Clean(comment, 2000, "O comentário");
        DecidedAt      = nowUtc;
        ChosenVia      = channel;
        Status         = BuildSelectionStatus.Submitted;
        SetUpdatedAt();
    }

    /// <summary>The company confirms the submitted choice (or records and confirms one while Open).</summary>
    public void Confirm(Guid? optionId, string? confirmedByName, DateTime nowUtc)
    {
        if (Status == BuildSelectionStatus.Confirmed) throw new ConflictException("Esta escolha já foi confirmada.");
        if (Status == BuildSelectionStatus.Cancelled)
            throw new DomainException("Esta escolha foi cancelada.", "selection_cancelled");
        if (Status == BuildSelectionStatus.Open)
        {
            if (optionId is not { } chosen || chosen == Guid.Empty)
                throw new DomainException("O cliente ainda não escolheu: informe a opção para confirmar.", "selection_option_required");
            ChosenOptionId = chosen;
            DecidedAt      = nowUtc;
            ChosenVia      = BuildDecisionChannel.Internal;
        }
        else if (optionId is { } other && other != ChosenOptionId)
        {
            throw new DomainException("A opção informada não é a escolhida pelo cliente.", "selection_option_mismatch");
        }
        Status          = BuildSelectionStatus.Confirmed;
        ConfirmedAt     = nowUtc;
        ConfirmedByName = string.IsNullOrWhiteSpace(confirmedByName) ? "usuário" : confirmedByName.Trim();
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status == BuildSelectionStatus.Confirmed) throw new ConflictException("Esta escolha já foi confirmada.");
        Status = BuildSelectionStatus.Cancelled;
        SetUpdatedAt();
    }

    public void LinkChangeOrder(Guid changeOrderId)
    {
        ChangeOrderId = changeOrderId;
        SetUpdatedAt();
    }

    public void EnsureOpen()
    {
        if (Status != BuildSelectionStatus.Open)
            throw new DomainException("Só escolhas em aberto podem ser alteradas.", "selection_closed");
    }
}

/// <summary>An option of a selection: price relative to the standard option, optional image and lead time.</summary>
public class BuildSelectionOption : TenantEntity
{
    private BuildSelectionOption() { }
    private BuildSelectionOption(Guid tenantId) : base(tenantId) { }

    public Guid     SelectionId  { get; private set; }
    /// <summary>Opaque id used by the client portal.</summary>
    public Guid     PublicId     { get; private set; }
    public int      Order        { get; private set; }
    public string   Name         { get; private set; } = string.Empty;
    public string?  Description  { get; private set; }
    /// <summary>Price difference to the standard option (0 for the standard; may be negative).</summary>
    public decimal  PriceDelta   { get; private set; }
    public bool     IsStandard   { get; private set; }
    /// <summary>Private storage key (upload context build-selection-option).</summary>
    public string?  ImageKey     { get; private set; }
    public int?     LeadTimeDays { get; private set; }

    public static BuildSelectionOption Create(Guid tenantId, Guid selectionId, int order, string name,
        string? description, decimal priceDelta, bool isStandard, string? imageKey, int? leadTimeDays)
    {
        if (selectionId == Guid.Empty) throw new DomainException("Escolha inválida.");
        var o = new BuildSelectionOption(tenantId) { SelectionId = selectionId, PublicId = Guid.NewGuid() };
        o.Update(order, name, description, priceDelta, isStandard, imageKey, leadTimeDays);
        return o;
    }

    public void Update(int order, string name, string? description, decimal priceDelta, bool isStandard,
        string? imageKey, int? leadTimeDays)
    {
        if (Math.Abs(priceDelta) > 1_000_000_000m) throw new DomainException("Diferença de preço fora do limite.");
        if (leadTimeDays is < 0 or > 3650) throw new DomainException("O prazo de entrega precisa ficar entre 0 e 3650 dias.");
        Order        = Math.Max(0, order);
        Name         = BuildMoney.Required(name, 200, "O nome da opção");
        Description  = BuildMoney.Clean(description, 2000, "A descrição");
        PriceDelta   = BuildMoney.Round(priceDelta);
        IsStandard   = isStandard;
        ImageKey     = BuildMoney.Clean(imageKey, 500, "A imagem");
        LeadTimeDays = leadTimeDays;
        SetUpdatedAt();
    }

    public void ClearStandard()
    {
        if (!IsStandard) return;
        IsStandard = false;
        SetUpdatedAt();
    }

    /// <summary>Whether confirming this option changes the contract or the schedule (→ draft change order).</summary>
    public bool ChangesContract => PriceDelta != 0m || LeadTimeDays is > 0;
}
