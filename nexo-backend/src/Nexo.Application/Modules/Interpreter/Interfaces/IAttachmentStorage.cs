namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IAttachmentStorage
{
    // Stores the file and returns a provider-agnostic storage key (e.g. blob path or S3 key).
    Task<string> UploadAsync(
        Stream      content,
        string      fileName,
        string      contentType,
        Guid        tenantId,
        CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);

    // Verifies storage backend is reachable (used by health checks).
    Task PingAsync(CancellationToken ct = default);
}
