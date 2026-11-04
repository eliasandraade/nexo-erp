using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>Who produced a stored forecast.</summary>
public enum BuildForecastSource
{
    /// <summary>The daily job (with the progress snapshot), when the company has BuildPredict on.</summary>
    Scheduled = 1,
    /// <summary>A user asked for a recomputation (POST …/forecast).</summary>
    Manual    = 2,
}

/// <summary>
/// A stored delivery forecast of a project (Wave 6) — deterministic (methodology <see cref="Methodology"/>, e.g.
/// <c>productivity/1</c>), never typed. Immutable once written; the history is kept (one or more per day), so "what did we
/// forecast on day X" stays answerable. Per-stage figures and the factors are JSON (column type json).
/// </summary>
public class BuildForecast : TenantEntity
{
    private BuildForecast() { }
    private BuildForecast(Guid tenantId) : base(tenantId) { }

    public Guid                ProjectId        { get; private set; }
    /// <summary>Business day (Brazil) the forecast was computed for.</summary>
    public DateOnly            Date             { get; private set; }
    public DateTime            GeneratedAt      { get; private set; }
    public string              Methodology      { get; private set; } = string.Empty;
    public int                 Version          { get; private set; }
    public DateOnly?           ForecastDelivery { get; private set; }
    public DateOnly?           PlannedDelivery  { get; private set; }
    public decimal             Confidence       { get; private set; }
    public string              FactorsJson      { get; private set; } = "[]";
    public string              StagesJson       { get; private set; } = "[]";
    public BuildForecastSource Source           { get; private set; }
    public Guid?               CreatedBy        { get; private set; }
    public string?             CreatedByName    { get; private set; }

    public static BuildForecast Store(Guid tenantId, Guid projectId, DateOnly date, string methodology, int version,
        DateOnly? forecastDelivery, DateOnly? plannedDelivery, decimal confidence, string factorsJson, string stagesJson,
        BuildForecastSource source, Guid? createdBy = null, string? createdByName = null)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (string.IsNullOrWhiteSpace(methodology)) throw new DomainException("Metodologia da previsão obrigatória.");
        if (!Enum.IsDefined(source)) throw new DomainException("Origem da previsão inválida.");
        return new BuildForecast(tenantId)
        {
            ProjectId        = projectId,
            Date             = date,
            GeneratedAt      = DateTime.UtcNow,
            Methodology      = methodology.Trim(),
            Version          = Math.Max(1, version),
            ForecastDelivery = forecastDelivery,
            PlannedDelivery  = plannedDelivery,
            Confidence       = Math.Clamp(confidence, 0m, 1m),
            FactorsJson      = string.IsNullOrWhiteSpace(factorsJson) ? "[]" : factorsJson,
            StagesJson       = string.IsNullOrWhiteSpace(stagesJson) ? "[]" : stagesJson,
            Source           = source,
            CreatedBy        = createdBy,
            CreatedByName    = string.IsNullOrWhiteSpace(createdByName) ? null : createdByName.Trim(),
        };
    }
}
