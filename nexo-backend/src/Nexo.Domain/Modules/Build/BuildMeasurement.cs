using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

public enum BuildMeasurementStatus
{
    Draft     = 1,
    Submitted = 2,
    Approved  = 3,
    Rejected  = 4,
}

/// <summary>
/// A measurement (medição) of a period: per stage, the cumulative % measured. Approving it is the only way a
/// measurement changes stage progress — through the same path as a manual progress update (history event with
/// source Measurement + today's snapshot). Approving twice is a conflict.
/// </summary>
public class BuildMeasurement : TenantEntity
{
    private BuildMeasurement() { }
    private BuildMeasurement(Guid tenantId) : base(tenantId) { }

    public Guid                   ProjectId       { get; private set; }
    public int                    Number          { get; private set; }
    public DateOnly               PeriodStart     { get; private set; }
    public DateOnly               PeriodEnd       { get; private set; }
    public BuildMeasurementStatus Status          { get; private set; }
    public string?                Notes           { get; private set; }
    public DateTime?              SubmittedAt     { get; private set; }
    public Guid?                  ApprovedBy      { get; private set; }
    public string?                ApprovedByName  { get; private set; }
    public DateTime?              ApprovedAt      { get; private set; }
    public DateTime?              RejectedAt      { get; private set; }
    public string?                RejectedByName  { get; private set; }
    public string?                RejectionReason { get; private set; }
    public Guid                   CreatedBy       { get; private set; }
    public string                 CreatedByName   { get; private set; } = string.Empty;

    public static BuildMeasurement Create(Guid tenantId, Guid projectId, int number, DateOnly periodStart,
        DateOnly periodEnd, string? notes, Guid createdBy, string? createdByName)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (number < 1) throw new DomainException("Número da medição inválido.");
        var m = new BuildMeasurement(tenantId)
        {
            ProjectId     = projectId,
            Number        = number,
            Status        = BuildMeasurementStatus.Draft,
            CreatedBy     = createdBy,
            CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? "usuário" : createdByName.Trim(),
        };
        m.Apply(periodStart, periodEnd, notes);
        return m;
    }

    public void Update(DateOnly periodStart, DateOnly periodEnd, string? notes)
    {
        EnsureDraft();
        Apply(periodStart, periodEnd, notes);
        SetUpdatedAt();
    }

    private void Apply(DateOnly periodStart, DateOnly periodEnd, string? notes)
    {
        if (periodEnd < periodStart)
            throw new DomainException("O fim do período não pode ser antes do início.", "measurement_period_invalid");
        PeriodStart = periodStart;
        PeriodEnd   = periodEnd;
        Notes       = BuildMoney.Clean(notes, 4000, "As observações");
    }

    public void EnsureDraft()
    {
        if (Status != BuildMeasurementStatus.Draft)
            throw new DomainException("Só medições em rascunho podem ser alteradas.", "measurement_not_draft");
    }

    public void Submit(DateTime nowUtc)
    {
        EnsureDraft();
        Status      = BuildMeasurementStatus.Submitted;
        SubmittedAt = nowUtc;
        SetUpdatedAt();
    }

    /// <summary>Draft | Submitted → Approved. A second approval is a conflict.</summary>
    public void Approve(Guid approvedBy, string? approvedByName, DateTime nowUtc)
    {
        if (Status == BuildMeasurementStatus.Approved) throw new ConflictException("Esta medição já foi aprovada.");
        if (Status == BuildMeasurementStatus.Rejected)
            throw new DomainException("Esta medição foi recusada; crie outra.", "measurement_rejected");
        Status         = BuildMeasurementStatus.Approved;
        ApprovedBy     = approvedBy;
        ApprovedByName = string.IsNullOrWhiteSpace(approvedByName) ? "usuário" : approvedByName.Trim();
        ApprovedAt     = nowUtc;
        SetUpdatedAt();
    }

    public void Reject(string? rejectedByName, string? reason, DateTime nowUtc)
    {
        if (Status == BuildMeasurementStatus.Approved) throw new ConflictException("Esta medição já foi aprovada.");
        if (Status == BuildMeasurementStatus.Rejected) throw new ConflictException("Esta medição já foi recusada.");
        Status          = BuildMeasurementStatus.Rejected;
        RejectedAt      = nowUtc;
        RejectedByName  = string.IsNullOrWhiteSpace(rejectedByName) ? "usuário" : rejectedByName.Trim();
        RejectionReason = BuildMoney.Clean(reason, 1000, "O motivo");
        SetUpdatedAt();
    }
}

/// <summary>
/// One stage of a measurement: the stage's % before (<see cref="PreviousPercent"/>) and the cumulative %
/// measured now (≥ previous, ≤ 100). Optional measured value.
/// </summary>
public class BuildMeasurementItem : TenantEntity
{
    private BuildMeasurementItem() { }
    private BuildMeasurementItem(Guid tenantId) : base(tenantId) { }

    public Guid     MeasurementId   { get; private set; }
    public Guid     StageId         { get; private set; }
    public int      PreviousPercent { get; private set; }
    public int      MeasuredPercent { get; private set; }
    public decimal? Amount          { get; private set; }

    public int DeltaPercent => MeasuredPercent - PreviousPercent;

    public static BuildMeasurementItem Create(Guid tenantId, Guid measurementId, Guid stageId, int previousPercent,
        int measuredPercent, decimal? amount)
    {
        if (measurementId == Guid.Empty || stageId == Guid.Empty) throw new DomainException("Etapa inválida.");
        Validate(previousPercent, measuredPercent);
        if (amount is < 0m) throw new DomainException("O valor medido não pode ser negativo.");
        return new BuildMeasurementItem(tenantId)
        {
            MeasurementId   = measurementId,
            StageId         = stageId,
            PreviousPercent = previousPercent,
            MeasuredPercent = measuredPercent,
            Amount          = amount is { } a ? BuildMoney.Round(a) : null,
        };
    }

    /// <summary>
    /// Measured progress is cumulative: between 0 and 100 and never below what the stage already had.
    /// </summary>
    public static void Validate(int previousPercent, int measuredPercent)
    {
        if (previousPercent is < 0 or > 100) throw new DomainException("Progresso anterior inválido.");
        if (measuredPercent is < 0 or > 100)
            throw new DomainException("O percentual medido precisa ficar entre 0 e 100.", "measurement_percent_range");
        if (measuredPercent < previousPercent)
            throw new DomainException(
                $"O percentual medido ({measuredPercent}%) não pode ser menor que o já registrado na etapa ({previousPercent}%).",
                "measurement_below_previous");
    }

    /// <summary>At approval the stage may have moved since the draft: the item is re-checked against it.</summary>
    public void RefreshPrevious(int currentStagePercent)
    {
        Validate(currentStagePercent, MeasuredPercent);
        PreviousPercent = currentStagePercent;
        SetUpdatedAt();
    }
}
