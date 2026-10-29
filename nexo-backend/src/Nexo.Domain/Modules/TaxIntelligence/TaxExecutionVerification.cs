using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// The second milestone of an opportunity: after the operation, whether it really happened as the
/// scenario was approved (authorised fiscal document, protocol, final amounts, the commercial condition
/// actually applied). Sealed like the decision: its dossier carries the decision's hash and its own.
/// Immutable; one per opportunity.
/// </summary>
public class TaxExecutionVerification : StoreEntity
{
    private TaxExecutionVerification() { }   // EF Core
    private TaxExecutionVerification(Guid tenantId) : base(tenantId) { }

    public Guid OpportunityId { get; private set; }
    public Guid DecisionId { get; private set; }
    public TaxExecutionOutcome Outcome { get; private set; }
    /// <summary>Reference of the fiscal document of the operation (e.g. NF-e access key), as given.</summary>
    public string? FiscalDocumentReference { get; private set; }
    public string? AuthorizationProtocol { get; private set; }
    public string? Notes { get; private set; }
    public string DossierJson { get; private set; } = "{}";
    public string DossierHash { get; private set; } = string.Empty;
    public Guid VerifiedById { get; private set; }
    public string VerifiedByName { get; private set; } = string.Empty;
    public DateTime VerifiedAt { get; private set; }

    public static TaxExecutionVerification Create(
        Guid tenantId, Guid opportunityId, Guid decisionId, TaxExecutionOutcome outcome, string? fiscalDocumentReference,
        string? authorizationProtocol, string? notes, string dossierJson, string dossierHash, Guid verifiedById, string verifiedByName,
        DateTime verifiedAtUtc)
        => new(tenantId)
        {
            OpportunityId = opportunityId,
            DecisionId = decisionId,
            Outcome = outcome,
            FiscalDocumentReference = string.IsNullOrWhiteSpace(fiscalDocumentReference) ? null : fiscalDocumentReference.Trim(),
            AuthorizationProtocol = string.IsNullOrWhiteSpace(authorizationProtocol) ? null : authorizationProtocol.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            DossierJson = dossierJson,
            DossierHash = dossierHash,
            VerifiedById = verifiedById,
            VerifiedByName = verifiedByName,
            VerifiedAt = verifiedAtUtc,
        };
}
