namespace Nexo.Application.Features.Fiscal;

/// <summary>
/// Where notas are issued. The 1.0 has no provider: <see cref="NotConfiguredFiscalProvider"/> answers
/// every request with <see cref="FiscalIssueStatus.NotAvailable"/>. A real one (Focus NFe, NFE.io…)
/// plugs in here, behind <c>Integrations:Features:FiscalEnabled</c>, with per-tenant credentials and
/// the A1 certificate. A sale never waits on it: a failed issue leaves the sale done and the nota
/// pending, to retry (see docs/ORKEN_INTEGRATIONS_PLAN.md).
/// </summary>
public interface IFiscalDocumentProvider
{
    /// <summary>Provider name for logs and the readiness screen ("none" when there is none).</summary>
    string Name { get; }

    /// <summary>True when credentials and certificate are in place and the provider can be called.</summary>
    bool IsConfigured { get; }

    /// <summary>Issues the consumer nota (NFC-e) of a confirmed sale.</summary>
    Task<FiscalIssueResult> IssueConsumerNoteAsync(FiscalIssueRequest request, CancellationToken ct);
}

public enum FiscalIssueStatus { NotAvailable, Pending, Authorized, Rejected }

public record FiscalIssueRequest(Guid TenantId, Guid StoreId, Guid SaleId);

/// <summary>Outcome of an issue attempt. Access key and series/number only when authorized.</summary>
public record FiscalIssueResult(
    FiscalIssueStatus Status,
    string? Message = null,
    string? AccessKey = null,
    int? Series = null,
    long? Number = null);

/// <summary>The 1.0 default: nothing is issued and nothing leaves the system.</summary>
public sealed class NotConfiguredFiscalProvider : IFiscalDocumentProvider
{
    public string Name => "none";
    public bool IsConfigured => false;

    public Task<FiscalIssueResult> IssueConsumerNoteAsync(FiscalIssueRequest request, CancellationToken ct)
        => Task.FromResult(new FiscalIssueResult(FiscalIssueStatus.NotAvailable,
            "A emissão de nota fiscal ainda não está disponível."));
}
