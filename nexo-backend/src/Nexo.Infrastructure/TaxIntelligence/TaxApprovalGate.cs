using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Decides whether rule versions are approved for the configured <see cref="TaxApprovalMode"/> and
/// takes the governance snapshot a calculation or a decision records. The controlled pilot accepts the
/// internal pilot approval (or a professional one); the definitive mode, only a professional approval.
/// The snapshot always carries both dimensions, so a pending professional review stays visible.
/// </summary>
public sealed class TaxApprovalGate
{
    private readonly NexoDbContext _db;
    private readonly TaxIntelligencePolicyOptions _policy;

    public TaxApprovalGate(NexoDbContext db, IOptions<TaxIntelligencePolicyOptions> policy)
    {
        _db = db;
        _policy = policy.Value;
    }

    public TaxApprovalMode Mode => _policy.Mode;

    public async Task<TaxGovernanceDto> SnapshotAsync(IReadOnlyCollection<Guid> versionIds, DateOnly date, bool enforced, CancellationToken ct)
    {
        var rows = await _db.TaxRuleVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id))
            .Join(_db.TaxRules.AsNoTracking(), v => v.RuleId, r => r.Id, (v, r) => new { v, r.Key })
            .ToListAsync(ct);
        var rules = rows.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.v.Version)
            .Select(x => new TaxRuleGovernanceDto(x.v.Id, x.Key, x.v.Version,
                x.v.InternalApproval, x.v.IsApprovedForPilotOn(date), x.v.InternalApprovalCaveats,
                x.v.ProfessionalReview, x.v.IsProfessionallyApprovedOn(date), x.v.ProfessionalReviewCaveats))
            .ToList();
        return new TaxGovernanceDto(Mode, TaxGovernance.PolicyFor(Mode), enforced, TaxGovernance.NatureOf(Mode),
            Mode == TaxApprovalMode.ControlledPilot ? TaxGovernance.NotAProfessionalOpinion : "Somente aprovação profissional registrada é aceita.",
            rules);
    }

    public Task<TaxGovernanceDto> ForCalculationAsync(IReadOnlyCollection<Guid> versionIds, DateOnly date, CancellationToken ct)
        => SnapshotAsync(versionIds, date, _policy.RequireApprovedRulesForCalculations, ct);

    public Task<TaxGovernanceDto> ForDecisionAsync(IReadOnlyCollection<Guid> versionIds, DateOnly date, CancellationToken ct)
        => SnapshotAsync(versionIds, date, _policy.RequireApprovedRulesForDecisions, ct);

    /// <summary>The refusal message when rule versions do not satisfy the mode.</summary>
    public static string NotApprovedMessage(TaxGovernanceDto g) => g.Mode == TaxApprovalMode.ControlledPilot
        ? $"Regras sem aprovação interna para o piloto controlado: {string.Join(", ", g.NotApproved)}. Ativação técnica não é aprovação."
        : $"Regras ainda sem revisão tributária aprovada: {string.Join(", ", g.NotApproved)}. Ativação técnica não é aprovação profissional.";

    public static string NotApprovedCode(TaxGovernanceDto g)
        => g.Mode == TaxApprovalMode.ControlledPilot ? "tax_pilot_approval_missing" : "tax_professional_review_pending";
}
