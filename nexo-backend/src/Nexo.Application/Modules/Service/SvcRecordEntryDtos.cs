using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>Durable attachment metadata persisted in the record's jsonb column. No public URL.</summary>
public sealed record SvcRecordAttachmentInput(
    string  StorageKey,
    string? FileName    = null,
    string? ContentType = null,
    long?   SizeBytes   = null,
    string? Caption     = null);

/// <summary>Attachment as returned on read: durable fields + the URL resolved from StorageKey.</summary>
public sealed record SvcRecordAttachmentDto(
    string  StorageKey,
    string? FileName,
    string? ContentType,
    long?   SizeBytes,
    string? Caption,
    string? Url);

public sealed record SvcRecordEntryDto(
    Guid                                  Id,
    Guid                                  StoreId,
    SvcRecordContextType                  ContextType,
    Guid                                  ContextId,
    Guid                                  AuthorUserId,
    string?                               Text,
    IReadOnlyList<SvcRecordAttachmentDto> Attachments,
    DateTime                              CreatedAt);

public sealed record CreateSvcRecordEntryRequest(
    SvcRecordContextType?                    ContextType,
    Guid?                                    ContextId,
    string?                                  Text        = null,
    IReadOnlyList<SvcRecordAttachmentInput>? Attachments = null);
