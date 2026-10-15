using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;

namespace Nexo.Application.Integrations.Pdf;

/// <param name="TenantName">Who sold: the issuer's trade/legal name (Fiscal) or the company name.</param>
/// <param name="IssuerDocument">CNPJ of the issuer, digits only, when the fiscal profile is filled in.</param>
/// <param name="IssuerAddress">One-line address of the issuer, when known.</param>
/// <summary>Who appears at the top of a receipt.</summary>
public sealed record ReceiptHeader(string Name, string? Document, string? Address);

/// <summary>The issuer of the current store (Fiscal), or the company name when there is none.</summary>
public interface IReceiptHeaderProvider
{
    Task<ReceiptHeader> GetAsync(CancellationToken ct);
}

public sealed record SaleReceiptRequest(SaleDto Sale, string TenantName, string? IssuerDocument = null, string? IssuerAddress = null);
public sealed record CashCloseReportRequest(CashSessionDto Session, string TenantName);
public sealed record ProductSheetRequest(ProductDto Product, string TenantName);
