using Nexo.Application.Integrations.DTOs;

namespace Nexo.Application.Integrations.Contracts;

public interface IStorageProvider
{
    /// <summary>
    /// Uploads a file and returns the storage key and public URL.
    /// Throws on failure — caller handles the exception.
    /// </summary>
    Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file by key. Tolerant — does not throw if key not found.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Verifies storage backend is reachable.</summary>
    Task PingAsync(CancellationToken ct = default);
}
