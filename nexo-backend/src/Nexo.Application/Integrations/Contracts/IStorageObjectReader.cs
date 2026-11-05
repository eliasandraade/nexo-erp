namespace Nexo.Application.Integrations.Contracts;

/// <summary>
/// Reads an object's bytes back from storage, for files the API streams itself (private documents that must be
/// authorised on every download and never handed out as a URL).
/// </summary>
public interface IStorageObjectReader
{
    /// <summary>The object's bytes, read fully into memory (callers bound the size when storing). Throws if missing.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
}
