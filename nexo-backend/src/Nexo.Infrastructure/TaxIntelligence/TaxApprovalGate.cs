using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>Why new work was refused before any rule ran: a stable code and a plain message.</summary>
public sealed record TaxGateRefusal(string Code, string Message);

/// <summary>
/// The fail-closed gate of Tax Intelligence. New work (calculation, simulation, detection, decision,
/// execution verification) passes only if every condition holds:
/// <list type="number">
/// <item>the global kill switch is on (<see cref="TaxIntelligencePolicyOptions.Enabled"/>);</item>
/// <item>where approval is enforced, an approval mode is configured (never a fallback);</item>
/// <item>in the controlled pilot, the tenant/store is inside the platform-kept pilot scope, within its validity;</item>
/// <item>every rule the work rests on is technically active and approved for the mode.</item>
/// </list>
/// Reading and reproducing what was recorded never goes through it. The governance snapshot keeps both
/// approval dimensions of each rule, so a pending professional review always stays visible.
/// </summary>
public sealed class TaxApprovalGate
{
    private readonly NexoDbContext _db;
    private readonly TaxIntelligencePolicyOptions _policy;
    private readonly ICurrentTenant _tenant;
    private readonly ICurrentUser _user;
    private readonly TaxModuleState _state;

    public TaxApprovalGate(NexoDbContext db, IOptions<TaxIntelligencePolicyOptions> policy, ICurrentTenant tenant, ICurrentUser user, TaxModuleState state)
    {
        _db = db;
        _policy = policy.Value;
        _tenant = tenant;
        _user = user;
        _state = state;
    }

    public TaxApprovalMode Mode => _policy.Mode;

    /// <summary>The installation policy (maker-checker by risk, materiality default) the gate enforces.</summary>
    public TaxIntelligencePolicyOptions Policy => _policy;

    /// <summary>The Brazilian business day — the same day the screens and the calculations use.</summary>
    private static DateOnly Today => DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));

    /// <summary>Admission of a new calculation or simulation (null = admitted).</summary>
    public Task<TaxGateRefusal?> AdmitCalculationAsync(CancellationToken ct) => AdmitAsync(_policy.RequireApprovedRulesForCalculations, ct);

    /// <summary>Admission of a new decision or execution verification (null = admitted).</summary>
    public Task<TaxGateRefusal?> AdmitDecisionAsync(CancellationToken ct) => AdmitAsync(_policy.RequireApprovedRulesForDecisions, ct);

    // Messages are shown to company users: they say what is going on, never configuration keys.
    private async Task<TaxGateRefusal?> AdmitAsync(bool enforced, CancellationToken ct)
    {
        if (!_policy.Enabled)
            return new("tax_intelligence_disabled", "A inteligência tributária está desligada nesta instalação.");
        if (_state.ConfigurationProblems.Count > 0)
            return new("tax_configuration_invalid", "A configuração da inteligência tributária desta instalação é inválida: nada é executado até ser corrigida.");
        var latest = await _state.LatestAsync(ct);
        if (latest?.Status == TaxModuleStatus.Suspended)
            return new("tax_module_suspended", "A inteligência tributária está suspensa pela plataforma: nenhum trabalho novo é executado. O histórico continua disponível.");
        if (!enforced) return null;
        if (_policy.Mode == TaxApprovalMode.Disabled)
            return new("tax_approval_mode_disabled", "Nenhum modo de aprovação está configurado nesta instalação: nada é executado.");

        if (_policy.Mode == TaxApprovalMode.ControlledPilot)
        {
            var scope = await LoadScopeAsync(ct);
            if (scope is null) return new("tax_pilot_scope_missing", "O piloto controlado não foi ativado pela plataforma.");
            var refusal = scope.Refusal(_tenant.Id, _user.StoreId, Today);
            if (refusal is not null) return new(refusal.Value.Code, refusal.Value.Message);
        }
        // The configuration allows a mode; the platform still has to activate that very mode.
        if (latest is not { Status: TaxModuleStatus.Active } || latest.Mode != _policy.Mode)
            return new("tax_module_not_activated", "A plataforma ainda não ativou este modo da inteligência tributária: nada é executado.");
        return null;
    }

    private Task<TaxPilotScope?> LoadScopeAsync(CancellationToken ct)
        => _db.TaxPilotScopes.AsNoTracking().Include(s => s.Participants).OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);

    /// <param name="regime">The establishment regime of the work (a <c>TaxRegime</c> name): a professional approval holds only for the regimes it covers.</param>
    public async Task<TaxGovernanceDto> SnapshotAsync(IReadOnlyCollection<Guid> versionIds, DateOnly date, bool enforced, CancellationToken ct, string? regime = null)
    {
        var rows = await _db.TaxRuleVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id))
            .Join(_db.TaxRules.AsNoTracking(), v => v.RuleId, r => r.Id, (v, r) => new { v, r.Key })
            .ToListAsync(ct);
        var rules = rows.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.v.Version)
            .Select(x => new TaxRuleGovernanceDto(x.v.Id, x.Key, x.v.Version, x.v.Status,
                x.v.InternalApproval, x.v.IsApprovedForPilotOn(date), x.v.InternalApprovalPolicy,
                x.v.InternalApprovedById, x.v.InternalApprovedBy, x.v.InternalApprovedByRole, x.v.InternalApprovedAt,
                x.v.InternalApprovalScope, x.v.InternalApprovalValidFrom, x.v.InternalApprovalValidTo, x.v.InternalApprovalCaveats,
                x.v.InternalApprovalDecisionRef,
                x.v.ProfessionalReview, x.v.IsProfessionallyApprovedOn(date, regime), x.v.ProfessionalReviewScope, x.v.ProfessionalReviewCaveats,
                x.v.ProfessionalReviewRecordId, x.v.ProfessionalReviewedBy, x.v.ProfessionalReference, x.v.ProfessionalReviewValidFrom,
                x.v.ProfessionalReviewValidTo, x.v.ProfessionalReviewRegimes))
            .ToList();
        var mode = _policy.Mode;
        var scope = mode == TaxApprovalMode.ControlledPilot ? await LoadScopeAsync(ct) : null;
        return new TaxGovernanceDto(mode, TaxGovernance.PolicyFor(mode), enforced, TaxGovernance.NatureOf(mode), TaxGovernance.NoticeOf(mode),
            mode == TaxApprovalMode.ControlledPilot ? TaxGovernance.ShadowNotice : null,
            scope is null ? null : new TaxPilotScopeSnapshotDto(scope.DecisionReference, scope.ValidFrom, scope.ValidTo, scope.ActivatedByName, scope.ActivatedAt),
            rules);
    }

    public Task<TaxGovernanceDto> ForCalculationAsync(IReadOnlyCollection<Guid> versionIds, DateOnly date, CancellationToken ct, string? regime = null)
        => SnapshotAsync(versionIds, date, _policy.RequireApprovedRulesForCalculations, ct, regime);

    public Task<TaxGovernanceDto> ForDecisionAsync(IReadOnlyCollection<Guid> versionIds, DateOnly date, CancellationToken ct, string? regime = null)
        => SnapshotAsync(versionIds, date, _policy.RequireApprovedRulesForDecisions, ct, regime);

    /// <summary>Where this tenant/store stands today, for the screens (no rule involved).</summary>
    public async Task<TaxGovernanceStatusDto> StatusAsync(CancellationToken ct)
    {
        var refusal = await AdmitCalculationAsync(ct);
        var scope = _policy.Mode == TaxApprovalMode.ControlledPilot ? await LoadScopeAsync(ct) : null;
        var latest = await _state.LatestAsync(ct);
        return new TaxGovernanceStatusDto(_policy.Enabled, _policy.Mode, TaxGovernance.PolicyFor(_policy.Mode),
            refusal is null, refusal?.Code, refusal?.Message,
            _policy.Mode == TaxApprovalMode.ControlledPilot ? TaxGovernance.ShadowNotice : null,
            scope is null ? null : new TaxPilotScopeSnapshotDto(scope.DecisionReference, scope.ValidFrom, scope.ValidTo, scope.ActivatedByName, scope.ActivatedAt),
            latest?.Status ?? TaxModuleStatus.Inactive);
    }

    /// <summary>The refusal when rule versions do not back new work in the mode.</summary>
    public static TaxGateRefusal NotApproved(TaxGovernanceDto g) => g.Mode switch
    {
        TaxApprovalMode.ControlledPilot => new("tax_pilot_approval_missing",
            $"Regras sem aprovação interna válida para o piloto controlado (ou não mais ativas): {string.Join(", ", g.NotApproved)}. Ativação técnica não é aprovação."),
        TaxApprovalMode.ProfessionalFinal => new("tax_professional_review_pending",
            $"Regras ainda sem revisão tributária aprovada (ou não mais ativas): {string.Join(", ", g.NotApproved)}. Ativação técnica não é aprovação profissional."),
        _ => new("tax_approval_mode_disabled", "Nenhum modo de aprovação está configurado: nenhuma regra é aceita."),
    };
}
