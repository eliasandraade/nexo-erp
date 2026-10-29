namespace Nexo.Application.Modules.TaxIntelligence.Engine;

/// <summary>
/// Codes of the legal sources the code refers to by name. The sources themselves (literal excerpt,
/// URL, verification) are rows of tax_legal_sources seeded by migration; these constants only keep
/// the references in code and catalogue in sync.
/// </summary>
public static class TaxLegalCodes
{
    public const string Lc214Art10 = "LC214-2025-ART10";
    public const string Lc214Art12Base = "LC214-2025-ART12-CAPUT-P1";
    public const string Lc214Art12UnconditionalDiscount = "LC214-2025-ART12-P2-III-P3";
    public const string Lc214Art12TransitionTaxes = "LC214-2025-ART12-P2-V";
    public const string Lc214Art31SplitPayment = "LC214-2025-ART31";
    public const string Lc214Art47Credits = "LC214-2025-ART47";
    public const string Lc214Art343 = "LC214-2025-ART343";
    public const string Lc214Art346 = "LC214-2025-ART346";
    public const string Lc214Art348 = "LC214-2025-ART348";
    public const string Lc214Art349 = "LC214-2025-ART349";
}

/// <summary>Codes of the notes the catalogue rules emit — the simulator and detectors read them.</summary>
public static class TaxNoteCodes
{
    public const string Simples2026 = "IBSCBS-2026-SIMPLES";
    public const string Compensation2026 = "IBSCBS-2026-COMPENSATION";
    public const string Waiver2026 = "IBSCBS-2026-WAIVER";
    public const string ReferenceRatePending = "IBSCBS-REFERENCE-RATE-PENDING";

    /// <summary>Notes meaning the amount computed does not leave the company's cash in the period.</summary>
    public static readonly IReadOnlySet<string> NoCashEffect = new HashSet<string>(StringComparer.Ordinal) { Compensation2026, Waiver2026 };
}
