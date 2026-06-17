namespace Nexo.Application.Modules.Service;

/// <summary>Wire shape of a service professional. StoreId is exposed (Service is store-scoped); TenantId is not.</summary>
public sealed record SvcProfessionalDto(
    Guid     Id,
    Guid     StoreId,
    string   Name,
    string?  Role,
    string?  Specialty,
    string?  Color,
    string?  Phone,
    string?  Email,
    decimal? DefaultCommissionPercent,
    Guid?    UserId,
    bool     IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateSvcProfessionalRequest(
    string   Name,
    string?  Role = null,
    string?  Specialty = null,
    string?  Color = null,
    string?  Phone = null,
    string?  Email = null,
    decimal? DefaultCommissionPercent = null,
    Guid?    UserId = null);

/// <summary>Update editable details + commission in one PUT. UserId is set only at creation in v1.</summary>
public sealed record UpdateSvcProfessionalRequest(
    string   Name,
    string?  Role = null,
    string?  Specialty = null,
    string?  Color = null,
    string?  Phone = null,
    string?  Email = null,
    decimal? DefaultCommissionPercent = null);
