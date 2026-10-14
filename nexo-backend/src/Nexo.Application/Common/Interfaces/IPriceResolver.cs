namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// The unit price a product sells for — decided by the server (price lists, customer, validity),
/// never taken from the client.
/// </summary>
public interface IPriceResolver
{
    Task<decimal> ResolveUnitPriceAsync(Guid productId, Guid? customerId, CancellationToken ct = default);
}
