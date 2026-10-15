namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Read side of the Service finance panel (current store, query filters on).</summary>
public interface ISvcFinanceQueries
{
    /// <summary>Everything the panel needs for [fromUtc, toUtc).</summary>
    Task<SvcFinanceRaw> LoadAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
