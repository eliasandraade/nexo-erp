using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Gathers what a calculation or a simulation needs about an operation on its date: the profile version in force, the
/// ancillary-obligation assessment in force (only a verification counts), the classification catalogue for its codes, and
/// the facts of a confirmed sale. One place, so a calculation and a simulation read the same facts the same way.
/// </summary>
public sealed class TaxInputBuilder
{
    private readonly TaxProfileService _profiles;
    private readonly TaxComplianceService _compliance;
    private readonly TaxClassificationService _classifications;
    private readonly SaleTaxFactsReader _sales;

    public TaxInputBuilder(TaxProfileService profiles, TaxComplianceService compliance, TaxClassificationService classifications, SaleTaxFactsReader sales)
    {
        _profiles = profiles;
        _compliance = compliance;
        _classifications = classifications;
        _sales = sales;
    }

    public TaxComplianceService Compliance => _compliance;

    public Task<TaxOperationFacts> SaleAsync(Guid saleId, CancellationToken ct) => _sales.ReadAsync(saleId, ct);

    public Task<TaxProfileVersion> ProfileAsync(DateOnly date, CancellationToken ct) => _profiles.GetInForceAsync(date, ct);

    public async Task<(TaxEngineInput Input, TaxProfileVersion Profile, TaxComplianceAssessment? Compliance)> BuildAsync(
        TaxOperationRequest op, CancellationToken ct)
    {
        var profile = await _profiles.GetInForceAsync(op.OperationDate, ct);
        var compliance = await _compliance.InForceAsync(op.OperationDate, ct);
        return (TaxCalculationService.ToInput(op, profile, compliance?.ForRules), profile, compliance);
    }

    /// <summary>
    /// The coverage context of an operation: the establishment's regime and Simples situation from its profile, and the cClassTrib
    /// codes of its lines as the catalogue holds them on its date (professionally approved ones only count in the definitive mode).
    /// </summary>
    public async Task<TaxCoverageContext> CoverageContextAsync(TaxOperationRequest op, TaxProfileVersion profile, TaxApprovalMode mode,
        CancellationToken ct)
        => new(profile.Regime, profile.SimplesSituation, profile.IbsCbsRegularRegimeOption,
            await _classifications.CatalogueForAsync(op.Lines.Select(l => l.CClassTrib), op.OperationDate, ct),
            mode == TaxApprovalMode.ProfessionalFinal);
}
