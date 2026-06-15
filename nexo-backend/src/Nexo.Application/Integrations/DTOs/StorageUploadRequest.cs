namespace Nexo.Application.Integrations.DTOs;

public sealed record StorageUploadRequest(
    Stream Content,
    string FileName,
    string ContentType,
    string ObjectKey,
    long   ContentLength);
