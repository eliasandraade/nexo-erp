using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// A cost estimate for a project (or a standalone pre-sale estimate).
/// ProjectId is nullable — a budget can exist before a project is created.
/// When the client approves, Convert() links it to a project.
///
/// TotalCost and FinalPrice are computed from items (RecalculateTotals),
/// but also stored directly so they survive item edits in place.
/// </summary>
public class BuildBudget : TenantEntity
{
    private BuildBudget() { }
    private BuildBudget(Guid tenantId) : base(tenantId) { }

    public Guid?             ProjectId     { get; private set; }
    public string            Name          { get; private set; } = string.Empty;
    public BuildBudgetStatus Status        { get; private set; }
    public decimal           TotalCost     { get; private set; }
    public decimal           MarginPercent { get; private set; }
    public decimal           FinalPrice    { get; private set; }
    public Guid              CreatedBy     { get; private set; }

    // ── Wave 2: commercial terms, versions, decision trail ────────────────────
    /// <summary>Last day the offer is valid. A Sent budget past this day is expired and cannot be approved.</summary>
    public DateOnly?         ValidUntil      { get; private set; }
    /// <summary>Notes for the client (printed on the PDF).</summary>
    public string?           Notes           { get; private set; }
    /// <summary>Internal notes — never on the PDF, never on the portal.</summary>
    public string?           InternalNotes   { get; private set; }
    /// <summary>Built area the budget refers to (cost / price per m²).</summary>
    public decimal?          AreaM2          { get; private set; }
    /// <summary>1 for a new budget; a new version of a family is max + 1.</summary>
    public int               Version         { get; private set; } = 1;
    /// <summary>The budget this one is a new version of (null for the first one).</summary>
    public Guid?             ParentBudgetId  { get; private set; }
    /// <summary>The first budget of the version family (null when this one is the first).</summary>
    public Guid?             RootBudgetId    { get; private set; }
    /// <summary>The template the budget was created from, if any.</summary>
    public Guid?             SourceTemplateId { get; private set; }
    public DateTime?         ApprovedAt      { get; private set; }
    public Guid?             ApprovedBy      { get; private set; }
    public string?           ApprovedByName  { get; private set; }
    public DateTime?         RejectedAt      { get; private set; }
    public Guid?             RejectedBy      { get; private set; }
    public string?           RejectedByName  { get; private set; }
    public string?           RejectionReason { get; private set; }

    /// <summary>The id of the version family (the first budget's id).</summary>
    public Guid FamilyId => RootBudgetId ?? Id;

    // Navigation
    public ICollection<BuildBudgetItem> Items { get; private set; } = [];

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildBudget Create(
        Guid    tenantId,
        Guid    createdBy,
        string  name,
        Guid?   projectId     = null,
        decimal marginPercent = 0m)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Budget name is required.");
        if (createdBy == Guid.Empty)         throw new DomainException("CreatedBy is required.");
        if (marginPercent < 0)               throw new DomainException("Margin cannot be negative.");

        return new BuildBudget(tenantId)
        {
            ProjectId     = projectId,
            Name          = name.Trim(),
            Status        = BuildBudgetStatus.Draft,
            TotalCost     = 0m,
            MarginPercent = marginPercent,
            FinalPrice    = 0m,
            CreatedBy     = createdBy,
        };
    }

    // ── Totals ────────────────────────────────────────────────────────────────

    /// <summary>Recomputes TotalCost and FinalPrice from supplied item list.</summary>
    public void RecalculateTotals(IEnumerable<BuildBudgetItem> items)
    {
        TotalCost  = BuildMoney.Round(items.Sum(i => i.TotalCost));
        FinalPrice = BuildMoney.Round(TotalCost * (1 + MarginPercent / 100m));
        SetUpdatedAt();
    }

    public void SetMargin(decimal marginPercent)
    {
        if (marginPercent < 0) throw new DomainException("Margin cannot be negative.");
        MarginPercent = marginPercent;
        FinalPrice    = BuildMoney.Round(TotalCost * (1 + marginPercent / 100m));
        SetUpdatedAt();
    }

    // ── Commercial terms (Wave 2) ─────────────────────────────────────────────

    /// <summary>Validity, client notes, internal notes and area — while the budget is editable (Draft/Sent).</summary>
    public void SetTerms(DateOnly? validUntil, string? notes, string? internalNotes, decimal? areaM2)
    {
        EnsureEditable();
        if (areaM2 is < 0m) throw new DomainException("A área não pode ser negativa.");
        ValidUntil    = validUntil;
        Notes         = BuildMoney.Clean(notes, 4000, "As observações");
        InternalNotes = BuildMoney.Clean(internalNotes, 4000, "As observações internas");
        AreaM2        = areaM2;
        SetUpdatedAt();
    }

    public void MarkFromTemplate(Guid templateId)
    {
        if (templateId == Guid.Empty) throw new DomainException("Modelo inválido.");
        SourceTemplateId = templateId;
    }

    /// <summary>A Sent budget whose validity ended before <paramref name="today"/>.</summary>
    public bool IsExpiredOn(DateOnly today)
        => Status == BuildBudgetStatus.Sent && ValidUntil is { } v && v < today;

    /// <summary>A new Draft with the same terms (items are copied by the caller). Not part of this version family.</summary>
    public BuildBudget Duplicate(Guid createdBy, string? name = null)
    {
        var copy = Create(TenantId, createdBy, string.IsNullOrWhiteSpace(name) ? $"{Name} (cópia)" : name, ProjectId, MarginPercent);
        copy.CopyTermsFrom(this);
        return copy;
    }

    /// <summary>
    /// A new version of a Sent, Rejected or Approved budget: a Draft copy with <paramref name="version"/>
    /// (the family's max + 1) pointing to this one. This budget is left untouched.
    /// </summary>
    public BuildBudget NewVersion(Guid createdBy, int version)
    {
        if (Status is not (BuildBudgetStatus.Sent or BuildBudgetStatus.Rejected or BuildBudgetStatus.Approved))
            throw new DomainException("Só orçamentos enviados, aprovados ou recusados ganham nova versão; edite o rascunho.",
                "budget_version_not_allowed");
        if (version <= Version) throw new DomainException("Número de versão inválido.");
        var copy = Create(TenantId, createdBy, Name, ProjectId, MarginPercent);
        copy.CopyTermsFrom(this);
        copy.Version        = version;
        copy.ParentBudgetId = Id;
        copy.RootBudgetId   = FamilyId;
        return copy;
    }

    private void CopyTermsFrom(BuildBudget source)
    {
        ValidUntil       = source.ValidUntil;
        Notes            = source.Notes;
        InternalNotes    = source.InternalNotes;
        AreaM2           = source.AreaM2;
        SourceTemplateId = source.SourceTemplateId;
    }

    // ── State transitions ─────────────────────────────────────────────────────

    public void MarkSent()
    {
        if (Status != BuildBudgetStatus.Draft)
            throw new DomainException($"Only Draft budgets can be marked Sent. Current: {Status}.");
        Status = BuildBudgetStatus.Sent;
        SetUpdatedAt();
    }

    /// <summary>Draft | Sent → Approved, recording who and when. An expired Sent budget is refused.</summary>
    public void Approve(DateOnly today, Guid approvedBy, string? approvedByName)
    {
        if (Status is not (BuildBudgetStatus.Draft or BuildBudgetStatus.Sent))
            throw new DomainException($"Cannot approve budget in status '{Status}'.");
        if (IsExpiredOn(today))
            throw new DomainException(
                $"Este orçamento venceu em {ValidUntil:dd/MM/yyyy}. Crie uma nova versão com nova validade.", "budget_expired");
        Status         = BuildBudgetStatus.Approved;
        ApprovedAt     = DateTime.UtcNow;
        ApprovedBy     = approvedBy;
        ApprovedByName = string.IsNullOrWhiteSpace(approvedByName) ? "usuário" : approvedByName.Trim();
        SetUpdatedAt();
    }

    /// <summary>Draft | Sent → Rejected, recording who, when and (optionally) why.</summary>
    public void Reject(Guid rejectedBy, string? rejectedByName, string? reason = null)
    {
        if (Status is not (BuildBudgetStatus.Draft or BuildBudgetStatus.Sent))
            throw new DomainException($"Cannot reject budget in status '{Status}'.");
        Status          = BuildBudgetStatus.Rejected;
        RejectedAt      = DateTime.UtcNow;
        RejectedBy      = rejectedBy;
        RejectedByName  = string.IsNullOrWhiteSpace(rejectedByName) ? "usuário" : rejectedByName.Trim();
        RejectionReason = BuildMoney.Clean(reason, 1000, "O motivo");
        SetUpdatedAt();
    }

    /// <summary>
    /// Approved → Converted. Links to a project (which may have been created from this budget).
    /// </summary>
    public void Convert(Guid projectId)
    {
        if (Status != BuildBudgetStatus.Approved)
            throw new DomainException("Only Approved budgets can be converted to a project.");
        if (projectId == Guid.Empty)
            throw new DomainException("ProjectId is required when converting a budget.");
        ProjectId = projectId;
        Status    = BuildBudgetStatus.Converted;
        SetUpdatedAt();
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Budget name is required.");
        EnsureEditable();
        Name = name.Trim();
        SetUpdatedAt();
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    public bool IsEditable =>
        Status is BuildBudgetStatus.Draft or BuildBudgetStatus.Sent;

    private void EnsureEditable()
    {
        if (!IsEditable)
            throw new DomainException($"Budget '{Name}' in status '{Status}' cannot be modified.");
    }
}
