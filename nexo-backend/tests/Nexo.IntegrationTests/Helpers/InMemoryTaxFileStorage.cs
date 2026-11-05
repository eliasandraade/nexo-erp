using System.Collections.Concurrent;
using Nexo.Application.Integrations.Contracts;
using Nexo.Infrastructure.TaxIntelligence.Files;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Private tax file storage kept in memory for the suite (no object store in the test host). Same contract as production: private keys
/// only; <see cref="Corrupt"/> lets a test simulate storage that no longer matches what was recorded.
/// </summary>
public sealed class InMemoryTaxFileStorage : ITaxFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public TaxFileStorageState State { get; set; } = TaxFileStorageState.Available;

    public async Task SaveAsync(string key, Stream content, string contentType, long length, CancellationToken ct)
    {
        if (!StorageKeys.IsPrivate(key)) throw new InvalidOperationException("Tax files are always private.");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        _objects[key] = buffer.ToArray();
    }

    public Task<Stream> OpenAsync(string key, CancellationToken ct)
        => _objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
            : throw new FileNotFoundException("Object not found in the test store.", key);

    public bool Contains(string key) => _objects.ContainsKey(key);

    public void Corrupt(string key) => _objects[key] = "corrompido"u8.ToArray();
}
