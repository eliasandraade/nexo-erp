namespace Nexo.Application.Modules.Service;

/// <summary>Wire shape of a catalog item (serviço/procedimento/aula).</summary>
public sealed record SvcCatalogItemDto(
    Guid     Id,
    Guid     StoreId,
    string   Name,
    string?  Description,
    string?  Category,
    int      DurationMinutes,
    decimal  Price,
    decimal? CommissionPercent,
    bool     RequiresSubject,
    bool     IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateSvcCatalogItemRequest(
    string   Name,
    int      DurationMinutes,
    decimal  Price,
    string?  Description = null,
    string?  Category = null,
    decimal? CommissionPercent = null,
    bool     RequiresSubject = false);

public sealed record UpdateSvcCatalogItemRequest(
    string   Name,
    int      DurationMinutes,
    decimal  Price,
    string?  Description = null,
    string?  Category = null,
    decimal? CommissionPercent = null,
    bool     RequiresSubject = false);
