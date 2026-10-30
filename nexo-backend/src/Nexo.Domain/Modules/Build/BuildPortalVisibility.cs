namespace Nexo.Domain.Modules.Build;

/// <summary>
/// What the client portal of a project shows. Every section is off unless its flag is on. Safe defaults:
/// progress, stages, schedule, photos, client updates, contact and weather on; anything financial,
/// documents, the internal daily log, forecasts, risks, team and 3D off. A flag missing from stored JSON
/// takes its default (properties with initializers).
/// </summary>
public sealed record BuildPortalVisibility
{
    public bool OverallProgress       { get; init; } = true;
    public bool StageProgress         { get; init; } = true;
    public bool Schedule              { get; init; } = true;
    public bool ForecastDelivery      { get; init; }
    public bool DailyLogs             { get; init; }
    public bool Photos                { get; init; } = true;
    public bool Videos                { get; init; }
    public bool Documents             { get; init; }
    public bool Weather               { get; init; } = true;
    public bool FinancialSummary      { get; init; }
    public bool FinancialCategories   { get; init; }
    public bool FinancialTransactions { get; init; }
    public bool Receipts              { get; init; }
    public bool Model3d               { get; init; }
    public bool RealityCapture        { get; init; }
    public bool ClientUpdates         { get; init; } = true;
    public bool Risks                 { get; init; }
    public bool Team                  { get; init; }
    public bool Contact               { get; init; } = true;

    public static BuildPortalVisibility Defaults { get; } = new();
}
