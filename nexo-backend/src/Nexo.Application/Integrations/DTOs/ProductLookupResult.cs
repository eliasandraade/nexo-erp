namespace Nexo.Application.Integrations.DTOs;

public sealed record ProductLookupResult(
    string   Barcode,
    string   Name,
    string?  Brand,
    string?  ImageUrl,
    string?  Category,
    string?  Quantity,
    string?  Unit,
    string   SourceProvider,
    double?  Confidence
);
