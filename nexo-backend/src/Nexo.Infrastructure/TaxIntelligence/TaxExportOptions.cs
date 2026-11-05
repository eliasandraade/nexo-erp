namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Limits of the Tax Intelligence exports (section <c>TaxIntelligence:Export</c>). An export either carries every row its filters match
/// or is refused saying why — it is never truncated.
/// </summary>
public sealed class TaxExportOptions
{
    public const string Section = "TaxIntelligence:Export";

    /// <summary>The most split expectations one CSV export carries; above it the export is refused and the user narrows the filters.</summary>
    public int MaxSplitRows { get; set; } = 100_000;
}
