namespace Nexo.Application.Integrations.Contracts;

using Nexo.Application.Integrations.DTOs;

public interface IBarcodeProductLookupProvider
{
    Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken ct = default);
}
