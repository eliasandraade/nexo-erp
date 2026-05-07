using Nexo.Application.Modules.Interpreter.Interfaces;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// MVP stub: stores attachments in the local filesystem under wwwroot/attachments/{tenantId}/.
/// Replace with S3/blob storage in production.
/// </summary>
public sealed class LocalAttachmentStorage : IAttachmentStorage
{
    private readonly string _baseDir;

    public LocalAttachmentStorage(string baseDir)
        => _baseDir = baseDir;

    public async Task<string> UploadAsync(
        Stream            content,
        string            fileName,
        string            contentType,
        Guid              tenantId,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(_baseDir, tenantId.ToString());
        Directory.CreateDirectory(dir);

        var ext        = Path.GetExtension(fileName);
        var storageKey = $"{tenantId}/{Guid.NewGuid()}{ext}";
        var fullPath   = Path.Combine(_baseDir, storageKey);

        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);

        return storageKey;
    }

    public Task<string> GetPresignedUrlAsync(string storageKey, TimeSpan expiry, CancellationToken ct = default)
    {
        // Local filesystem: return a relative path; the API layer serves it via StaticFiles.
        var url = $"/attachments/{storageKey}";
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_baseDir, storageKey);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task PingAsync(CancellationToken ct = default)
    {
        // Verify the base directory is accessible.
        if (!Directory.Exists(_baseDir))
            throw new DirectoryNotFoundException($"Attachment base directory not found: {_baseDir}");
        return Task.CompletedTask;
    }
}
