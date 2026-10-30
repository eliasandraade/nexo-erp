namespace Nexo.Domain.Modules.Build;

/// <summary>
/// What the client portal of a project shows. Every section is off unless its flag is on. Safe defaults:
/// progress, stages, schedule, photos, client updates, contact and weather on; anything financial,
/// documents, the internal daily log, forecasts, risks, team, 3D and measurements off; change orders and
/// selections on (Wave 2); post-construction ("Meu Imóvel") on (Wave 3). A flag missing from stored JSON
/// takes its default (properties with initializers). The <see cref="PasswordOnlySections"/> only ever apply to a password
/// portal (<see cref="EffectiveFor"/>).
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
    // Wave 2 (contract v1.1)
    /// <summary>Change orders sent to the client, approved or rejected; deciding needs a password portal.</summary>
    public bool ChangeOrders          { get; init; } = true;
    /// <summary>The client's selections (options, choice); choosing needs a password portal.</summary>
    public bool Selections            { get; init; } = true;
    /// <summary>Approved measurements (values only together with <see cref="FinancialSummary"/>).</summary>
    public bool Measurements          { get; init; }
    // Wave 3 (contract v1.2)
    /// <summary>"Meu Imóvel" after the delivery: handover, warranty terms and technical assistance (opening needs a password portal).</summary>
    public bool PostConstruction      { get; init; } = true;

    public static BuildPortalVisibility Defaults { get; } = new();

    /// <summary>
    /// Sections that carry personal or residential data (the property and its handover, warranty and assistance requests;
    /// change orders; selections; documents; site captures). They are served only by a password portal: on a public portal
    /// they are off whatever their flag says (contract v1.5).
    /// </summary>
    public static IReadOnlyList<string> PasswordOnlySections { get; } =
        ["postConstruction", "changeOrders", "selections", "documents", "realityCapture"];

    /// <summary>What a portal with this access actually serves: on a public portal the <see cref="PasswordOnlySections"/> are off.</summary>
    public BuildPortalVisibility EffectiveFor(BuildPortalAccess access)
        => access == BuildPortalAccess.Password
            ? this
            : this with { PostConstruction = false, ChangeOrders = false, Selections = false, Documents = false, RealityCapture = false };
}
