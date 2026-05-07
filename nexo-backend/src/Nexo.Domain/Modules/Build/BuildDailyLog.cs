using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// Daily work log entry (diário de obra).
/// One per project per date — enforced at the DB level (unique index).
/// Photos are child entities; notes are free text.
/// </summary>
public class BuildDailyLog : TenantEntity
{
    private BuildDailyLog() { }
    private BuildDailyLog(Guid tenantId) : base(tenantId) { }

    public Guid    ProjectId      { get; private set; }
    public DateOnly Date          { get; private set; }
    public string? WeatherSummary { get; private set; }
    public string  Notes          { get; private set; } = string.Empty;
    public Guid    CreatedBy      { get; private set; }

    // Navigation
    public ICollection<BuildDailyLogPhoto> Photos { get; private set; } = [];

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildDailyLog Create(
        Guid    tenantId,
        Guid    projectId,
        DateOnly date,
        Guid    createdBy,
        string  notes,
        string? weatherSummary = null)
    {
        if (projectId == Guid.Empty)          throw new DomainException("ProjectId is required.");
        if (createdBy == Guid.Empty)          throw new DomainException("CreatedBy is required.");
        if (string.IsNullOrWhiteSpace(notes)) throw new DomainException("Notes are required for a daily log.");

        return new BuildDailyLog(tenantId)
        {
            ProjectId      = projectId,
            Date           = date,
            CreatedBy      = createdBy,
            Notes          = notes.Trim(),
            WeatherSummary = weatherSummary?.Trim(),
        };
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void Update(string notes, string? weatherSummary)
    {
        if (string.IsNullOrWhiteSpace(notes)) throw new DomainException("Notes cannot be empty.");
        Notes          = notes.Trim();
        WeatherSummary = weatherSummary?.Trim();
        SetUpdatedAt();
    }
}
