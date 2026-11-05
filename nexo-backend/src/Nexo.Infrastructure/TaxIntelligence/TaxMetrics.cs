using System.Diagnostics.Metrics;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Observability;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Counters of Tax Intelligence, on the Orken meter (exported with the rest when OpenTelemetry is on). Tags carry only
/// codes and kinds — never amounts, documents, names or tenant data.
/// </summary>
public sealed class TaxMetrics
{
    private readonly Counter<long> _calculations;
    private readonly Counter<long> _refusals;
    private readonly Counter<long> _coverage;
    private readonly Counter<long> _opportunities;
    private readonly Counter<long> _approvals;
    private readonly Counter<long> _executions;
    private readonly Counter<long> _credits;
    private readonly Counter<long> _split;
    private readonly Counter<long> _evidence;
    private readonly Counter<long> _verifications;

    public TaxMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(OrkenMetrics.MeterName);
        _calculations = meter.CreateCounter<long>("orken.tax.calculations", description: "Tax calculations stored (new, not reused).");
        _refusals = meter.CreateCounter<long>("orken.tax.refusals", description: "New tax work refused by the gate, by action and reason code.");
        _coverage = meter.CreateCounter<long>("orken.tax.coverage_refusals", description: "Operations outside the supported matrix, by coverage status.");
        _opportunities = meter.CreateCounter<long>("orken.tax.opportunities", description: "Opportunities identified, by detector.");
        _approvals = meter.CreateCounter<long>("orken.tax.approvals", description: "Approvals recorded, by kind.");
        _executions = meter.CreateCounter<long>("orken.tax.execution_verifications", description: "Execution verifications, by outcome.");
        _credits = meter.CreateCounter<long>("orken.tax.credit_transitions", description: "Credit stage changes and ledger movements, by type.");
        _split = meter.CreateCounter<long>("orken.tax.split", description: "Split settlements and reversals, and divergences found.");
        _evidence = meter.CreateCounter<long>("orken.tax.evidence_uploads", description: "Private evidence uploads, by result.");
        _verifications = meter.CreateCounter<long>("orken.tax.engine_verifications", description: "Reproductions of stored calculations, by result.");
    }

    public void Calculated(string source) => _calculations.Add(1, new KeyValuePair<string, object?>("source", source));
    public void Refused(string action, string code)
        => _refusals.Add(1, new KeyValuePair<string, object?>("action", action), new KeyValuePair<string, object?>("code", code));
    public void CoverageRefused(string status) => _coverage.Add(1, new KeyValuePair<string, object?>("status", status));
    public void OpportunityIdentified(string detector) => _opportunities.Add(1, new KeyValuePair<string, object?>("detector", detector));
    public void Approved(string kind) => _approvals.Add(1, new KeyValuePair<string, object?>("kind", kind));
    public void ExecutionVerified(string outcome) => _executions.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    public void CreditTransition(string type) => _credits.Add(1, new KeyValuePair<string, object?>("type", type));
    public void Split(string kind) => _split.Add(1, new KeyValuePair<string, object?>("kind", kind));
    public void EvidenceUpload(string result) => _evidence.Add(1, new KeyValuePair<string, object?>("result", result));
    public void EngineVerification(string result) => _verifications.Add(1, new KeyValuePair<string, object?>("result", result));
}

/// <summary>
/// What every Tax Intelligence service works with: the unit of work, the tenant, the audit trail (who acts) and the
/// metrics. Kept together so the services depend on their own collaborators, not on a long list of plumbing.
/// </summary>
public sealed class TaxContext
{
    public TaxContext(NexoDbContext db, ICurrentTenant tenant, TaxAuditTrail audit, TaxMetrics metrics)
    {
        Db = db;
        Tenant = tenant;
        Audit = audit;
        Metrics = metrics;
    }

    public NexoDbContext Db { get; }
    public ICurrentTenant Tenant { get; }
    public TaxAuditTrail Audit { get; }
    public TaxMetrics Metrics { get; }
}
