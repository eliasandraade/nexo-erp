using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// The project's progress on a calendar day (Brazil): planned, actual and forecast overall %, the
/// forecast delivery and the per-stage figures. One per project per day. Today's snapshot is refreshed
/// while the day lasts; past days are never changed — they answer "como estava a obra em X".
/// </summary>
public class BuildProgressSnapshot : TenantEntity
{
    private BuildProgressSnapshot() { }
    private BuildProgressSnapshot(Guid tenantId) : base(tenantId) { }

    public Guid      ProjectId            { get; private set; }
    public DateOnly  Date                 { get; private set; }
    public decimal?  PlannedPercent       { get; private set; }
    public decimal   ActualPercent        { get; private set; }
    public decimal?  ForecastPercent      { get; private set; }
    public DateOnly? ForecastDeliveryDate { get; private set; }
    /// <summary>JSON array of per-stage figures (column type json).</summary>
    public string    StagesJson           { get; private set; } = "[]";
    public string    Methodology          { get; private set; } = string.Empty;

    public static BuildProgressSnapshot Take(
        Guid tenantId, Guid projectId, DateOnly date, decimal? planned, decimal actual, decimal? forecast,
        DateOnly? forecastDelivery, string stagesJson, string methodology)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        var s = new BuildProgressSnapshot(tenantId) { ProjectId = projectId, Date = date };
        s.Apply(planned, actual, forecast, forecastDelivery, stagesJson, methodology);
        return s;
    }

    /// <summary>Refreshes the figures of the snapshot of <paramref name="today"/>; a past day is immutable.</summary>
    public void Refresh(DateOnly today, decimal? planned, decimal actual, decimal? forecast,
        DateOnly? forecastDelivery, string stagesJson, string methodology)
    {
        if (Date != today)
            throw new DomainException("O retrato de um dia que já passou não pode ser alterado.");
        Apply(planned, actual, forecast, forecastDelivery, stagesJson, methodology);
        SetUpdatedAt();
    }

    private void Apply(decimal? planned, decimal actual, decimal? forecast, DateOnly? forecastDelivery,
        string stagesJson, string methodology)
    {
        PlannedPercent       = planned;
        ActualPercent        = actual;
        ForecastPercent      = forecast;
        ForecastDeliveryDate = forecastDelivery;
        StagesJson           = string.IsNullOrWhiteSpace(stagesJson) ? "[]" : stagesJson;
        Methodology          = methodology;
    }
}
