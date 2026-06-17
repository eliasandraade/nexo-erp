namespace Nexo.Application.Modules.Service;

/// <summary>
/// Editable fields shared by the create and update catalog requests, so both can be
/// validated by a single rule set (see SvcValidators).
/// </summary>
public interface ISvcCatalogItemFields
{
    string   Name { get; }
    int      DurationMinutes { get; }
    decimal  Price { get; }
    string?  Description { get; }
    string?  Category { get; }
    decimal? CommissionPercent { get; }
    bool     RequiresSubject { get; }
}

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
    bool     RequiresSubject = false) : ISvcCatalogItemFields;

public sealed record UpdateSvcCatalogItemRequest(
    string   Name,
    int      DurationMinutes,
    decimal  Price,
    string?  Description = null,
    string?  Category = null,
    decimal? CommissionPercent = null,
    bool     RequiresSubject = false) : ISvcCatalogItemFields;
