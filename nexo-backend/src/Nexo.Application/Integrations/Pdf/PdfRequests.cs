using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;

namespace Nexo.Application.Integrations.Pdf;

public sealed record SaleReceiptRequest(SaleDto Sale, string TenantName);
public sealed record CashCloseReportRequest(CashSessionDto Session, string TenantName);
public sealed record ProductSheetRequest(ProductDto Product, string TenantName);
