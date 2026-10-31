using System.Collections.Concurrent;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Test-only object store: keeps uploaded bytes in memory and signs URLs with a fake host, so the real upload pipeline
/// (validation, private keys, signed URLs) can run in a host with storage enabled. Nothing leaves the test process.
/// </summary>
public sealed class InMemoryStorageProvider : IStorageProvider
{
    public const string Host = "https://storage.test/";

    public ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> Objects { get; } = new();

    /// <summary>Behaves like the object store with no private bucket: every "private/…" operation fails closed.</summary>
    public bool RefusePrivate { get; init; }

    public bool CanStorePrivateFiles => !RefusePrivate;

    private void Guard(string key)
    {
        if (RefusePrivate && StorageKeys.IsPrivate(key)) throw new PrivateStorageUnavailableException();
    }

    public async Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default)
    {
        Guard(request.ObjectKey);
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, ct);
        Objects[request.ObjectKey] = (buffer.ToArray(), request.ContentType);
        return new StorageUploadResult(request.ObjectKey, Host + request.ObjectKey);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Guard(key);
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        Guard(key);
        return Objects.TryGetValue(key, out var o)
            ? Task.FromResult<Stream>(new MemoryStream(o.Bytes, writable: false))
            : throw new FileNotFoundException("Object not found.", key);
    }

    public string GetSignedUrl(string key, TimeSpan ttl)
    {
        Guard(key);
        return $"{Host}{key}?signature=test&ttl={(int)ttl.TotalSeconds}";
    }
}
