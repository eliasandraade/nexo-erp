using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>Editable subject fields shared by create + update, validated by one rule set.</summary>
public interface ISvcSubjectFields
{
    SvcSubjectKind Kind { get; }
    string         DisplayName { get; }
    string?        MetadataJson { get; }
    string?        Notes { get; }
}

public sealed record SvcSubjectDto(
    Guid           Id,
    Guid           CustomerId,
    SvcSubjectKind Kind,
    string         DisplayName,
    string?        MetadataJson,
    string?        Notes,
    bool           IsActive,
    DateTime       CreatedAt,
    DateTime       UpdatedAt);

public sealed record CreateSvcSubjectRequest(
    Guid           CustomerId,
    SvcSubjectKind Kind,
    string         DisplayName,
    string?        MetadataJson = null,
    string?        Notes        = null) : ISvcSubjectFields;

/// <summary>Update editable details + metadata in one PUT. CustomerId is fixed at creation.</summary>
public sealed record UpdateSvcSubjectRequest(
    SvcSubjectKind Kind,
    string         DisplayName,
    string?        MetadataJson = null,
    string?        Notes        = null) : ISvcSubjectFields;
