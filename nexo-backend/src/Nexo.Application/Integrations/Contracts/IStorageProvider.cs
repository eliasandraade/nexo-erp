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

    /// <summary>
    /// A signed GET URL for the key, valid for <paramref name="ttl"/>. Computed locally (no request
    /// to the provider). Private keys ("private/…") are signed against the private bucket only.
    /// </summary>
    string GetSignedUrl(string key, TimeSpan ttl);

    /// <summary>
    /// Whether private keys ("private/…") have a private place to go. When false, every operation on a private key
    /// throws <see cref="PrivateStorageUnavailableException"/> — private content never falls back to a public bucket.
    /// </summary>
    bool CanStorePrivateFiles => true;

    /// <summary>
    /// Opens a stored object for reading (server-side processing, e.g. a BIM model). The caller disposes the stream.
    /// Providers that cannot read back throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        => throw new NotSupportedException("Este armazenamento não permite ler arquivos de volta.");
}
