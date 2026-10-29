using Microsoft.AspNetCore.Http;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Writes tax events to the existing audit trail (audit_records) with who, tenant, store, entity,
/// before/after when relevant and the request id, so an event can be tied to its logs. Staged in the
/// caller's unit of work — saved with the change it describes, or not at all.
/// </summary>
public sealed class TaxAuditTrail
{
    public const string EntityPrefix = "Tax";

    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _user;
    private readonly IHttpContextAccessor _http;

    public TaxAuditTrail(IAuditWriter audit, ICurrentUser user, IHttpContextAccessor http)
    {
        _audit = audit;
        _user = user;
        _http = http;
    }

    public Guid UserId => _user.UserId;
    public string UserName => string.IsNullOrWhiteSpace(_user.Name) ? "usuário" : _user.Name;
    /// <summary>The capability the authenticated user acts under (e.g. "Platform" for the platform operator).</summary>
    public string UserRole => string.IsNullOrWhiteSpace(_user.Role) ? "desconhecido" : _user.Role;

    public void Record(string action, string entityType, Guid entityId, string description, object? details = null,
        string severity = AuditSeverity.Info)
        => _audit.Stage(
            actionType: action,
            severity: severity,
            entityType: EntityPrefix + entityType,
            entityId: entityId.ToString(),
            description: description,
            tenantId: _user.TenantId,
            actorId: _user.UserId,
            actorName: UserName,
            metadata: new { storeId = _user.StoreId, requestId = _http.HttpContext?.TraceIdentifier, details });

    /// <summary>Platform-level change (catalogue): no tenant, actor type "platform".</summary>
    public void RecordPlatform(string action, string entityType, Guid entityId, string description, object? details = null)
        => _audit.Stage(
            actionType: action,
            severity: AuditSeverity.Warning,
            entityType: EntityPrefix + entityType,
            entityId: entityId.ToString(),
            description: description,
            tenantId: null,
            actorId: _user.UserId,
            actorName: UserName,
            actorType: "platform",
            metadata: new { requestId = _http.HttpContext?.TraceIdentifier, details });
}

/// <summary>Audit action names of the Tax Intelligence domain.</summary>
public static class TaxAuditActions
{
    public const string ProfileVersionCreated = "tax_profile_version_created";
    public const string CalculationCreated = "tax_calculation_created";
    public const string SimulationCreated = "tax_simulation_created";
    public const string OpportunityIdentified = "tax_opportunity_identified";
    public const string OpportunityReviewStarted = "tax_opportunity_review_started";
    public const string OpportunityRequirementAnswered = "tax_opportunity_requirement_answered";
    public const string OpportunityEvidenceAdded = "tax_opportunity_evidence_added";
    public const string OpportunityBlocked = "tax_opportunity_blocked";
    public const string OpportunityRejected = "tax_opportunity_rejected";
    public const string OpportunityApproved = "tax_opportunity_approved";
    public const string CreditRegistered = "tax_credit_registered";
    public const string CreditMovement = "tax_credit_movement";
    public const string SplitExpectationsCreated = "tax_split_expectations_created";
    public const string SplitSettlementRecorded = "tax_split_settlement_recorded";
    public const string SplitSettlementReversed = "tax_split_settlement_reversed";
    public const string LegalSourceCreated = "tax_legal_source_created";
    public const string LegalSourceVerified = "tax_legal_source_verified";
    public const string RuleCreated = "tax_rule_created";
    public const string RuleVersionCreated = "tax_rule_version_created";
    public const string RuleVersionStatusChanged = "tax_rule_version_status_changed";
    public const string RuleVersionProfessionalReview = "tax_rule_version_professional_review";
    public const string RuleVersionPilotApproval = "tax_rule_version_pilot_approval";
    public const string RuleVersionPilotApprovalWithdrawn = "tax_rule_version_pilot_approval_withdrawn";
    public const string DecisionRefused = "tax_decision_refused";
    public const string PilotScopeConfigured = "tax_pilot_scope_configured";
    public const string PilotParticipantAdded = "tax_pilot_participant_added";
    public const string PilotParticipantRemoved = "tax_pilot_participant_removed";
    public const string ComplianceAssessmentRecorded = "tax_compliance_assessment_recorded";
    public const string CalculationRefused = "tax_calculation_refused";
    public const string SimulationRefused = "tax_simulation_refused";
    public const string DetectionRefused = "tax_detection_refused";
    public const string OpportunityEvidenceReviewed = "tax_opportunity_evidence_reviewed";
    public const string OpportunityExecutionVerified = "tax_opportunity_execution_verified";
    public const string CreditStageChanged = "tax_credit_stage_changed";
}
