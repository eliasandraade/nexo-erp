using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.TaxIntelligence.Files;

/// <summary>Limits of the private tax files (section <c>TaxIntelligence:Evidence</c>).</summary>
public sealed class TaxFileOptions
{
    public const string Section = "TaxIntelligence:Evidence";

    /// <summary>Largest file accepted, in MB (1–25).</summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>When on, a file is accepted only if a configured scanner says it is clean.</summary>
    public bool RequireScan { get; set; }

    public long MaxBytes => Math.Clamp(MaxFileSizeMb, 1, 25) * 1024L * 1024L;
}

/// <summary>Whether private tax files can be stored and read right now.</summary>
public enum TaxFileStorageState
{
    Available = 1,
    /// <summary>Storage is switched off in this installation.</summary>
    Disabled = 2,
    /// <summary>Production without a private bucket: sensitive files are never put in the public one.</summary>
    PrivateBucketMissing = 3,
}

/// <summary>Where the bytes of private tax files live. Replaced by an in-memory store in tests.</summary>
public interface ITaxFileStorage
{
    TaxFileStorageState State { get; }
    Task SaveAsync(string key, Stream content, string contentType, long length, CancellationToken ct);
    Task<Stream> OpenAsync(string key, CancellationToken ct);
}

/// <summary>
/// The ORKEN object storage (R2), private keys only. In production a private bucket is mandatory — the same rule
/// as the other sensitive uploads; without it, upload and download refuse instead of falling back to the public bucket.
/// </summary>
public sealed class PrivateTaxFileStorage : ITaxFileStorage
{
    private readonly IStorageProvider _storage;
    private readonly IStorageObjectReader _reader;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly StorageOptions _options;
    private readonly IHostEnvironment _env;

    public PrivateTaxFileStorage(IStorageProvider storage, IStorageObjectReader reader, IIntegrationFeatureFlags flags,
        IOptions<StorageOptions> options, IHostEnvironment env)
    {
        _storage = storage;
        _reader = reader;
        _flags = flags;
        _options = options.Value;
        _env = env;
    }

    public TaxFileStorageState State
        => !_flags.StorageEnabled ? TaxFileStorageState.Disabled
            : _env.IsProduction() && string.IsNullOrWhiteSpace(_options.R2.PrivateBucketName) ? TaxFileStorageState.PrivateBucketMissing
            : TaxFileStorageState.Available;

    public async Task SaveAsync(string key, Stream content, string contentType, long length, CancellationToken ct)
    {
        Ensure(key);
        await _storage.UploadAsync(new StorageUploadRequest(content, Path.GetFileName(key), contentType, key, length), ct);
    }

    public Task<Stream> OpenAsync(string key, CancellationToken ct)
    {
        Ensure(key);
        return _reader.OpenReadAsync(key, ct);
    }

    private void Ensure(string key)
    {
        if (!StorageKeys.IsPrivate(key)) throw new InvalidOperationException("Tax files are always private.");
        if (State != TaxFileStorageState.Available)
            throw new DomainException(TaxFileStore.UnavailableMessage(State), "tax_file_storage_unavailable");
    }
}

/// <summary>Content scanner hook (antivirus or similar). The default records files as not scanned.</summary>
public interface ITaxFileScanner
{
    /// <summary>Null when no scanner is configured.</summary>
    string? Engine { get; }
    Task<TaxFileScanStatus> ScanAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken ct);
}

public sealed class NoTaxFileScanner : ITaxFileScanner
{
    public string? Engine => null;
    public Task<TaxFileScanStatus> ScanAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken ct)
        => Task.FromResult(TaxFileScanStatus.NotScanned);
}

/// <summary>A file type the module accepts, recognised by its first bytes — never by its name or the browser's claim.</summary>
public sealed record TaxFileKind(string ContentType, string Extension, string[] AcceptedExtensions);

/// <summary>
/// Recognises what a file really is from its bytes. Accepted: PDF, PNG, JPEG, WebP and XML (the authorised NF-e
/// file). Anything that could run in a browser — HTML, SVG, XML carrying script — is refused. The server never
/// opens, parses or executes a file: it only reads these bytes to classify it.
/// </summary>
public static class TaxFileInspector
{
    private static readonly TaxFileKind Pdf = new("application/pdf", ".pdf", [".pdf"]);
    private static readonly TaxFileKind Png = new("image/png", ".png", [".png"]);
    private static readonly TaxFileKind Jpeg = new("image/jpeg", ".jpg", [".jpg", ".jpeg"]);
    private static readonly TaxFileKind Webp = new("image/webp", ".webp", [".webp"]);
    private static readonly TaxFileKind Xml = new("application/xml", ".xml", [".xml"]);

    public static readonly IReadOnlyList<string> AcceptedExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".webp", ".xml"];

    /// <summary>What the bytes are, or null when the type is not accepted.</summary>
    public static TaxFileKind? Detect(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith("%PDF-"u8)) return Pdf;
        if (b.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        if (b.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return Jpeg;
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return Webp;
        return IsXml(b) ? Xml : null;
    }

    private static bool IsXml(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) b = b[3..];
        var i = 0;
        while (i < b.Length && b[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        return b[i..].StartsWith("<?xml"u8);
    }

    /// <summary>Markup a browser would render or run. Refused in any accepted file type that is text.</summary>
    private static readonly string[] ActiveMarkup = ["<svg", "<html", "<!doctype html", "<script", "javascript:", "<iframe", "<object", "<embed"];

    public static bool HasActiveContent(ReadOnlySpan<byte> b)
    {
        var text = Encoding.UTF8.GetString(b).ToLowerInvariant();
        return ActiveMarkup.Any(m => text.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>
    /// The original name as metadata only: no directory part, no control or path characters, at most 200
    /// characters. It is shown and offered as the download name — it never names anything in storage.
    /// </summary>
    public static string SafeName(string? fileName, string extension)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var clean = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray()).Trim().Trim('.');
        if (clean.Length == 0) clean = "arquivo" + extension;
        return clean.Length <= 200 ? clean : clean[..200];
    }
}

/// <summary>
/// Accepts a private tax file: checks size, type (by its bytes, matching the extension) and active content, runs
/// the scanner hook, computes the SHA-256 of the bytes actually received and stores them under an internal key
/// (a new GUID — never the original name). Returns what was measured; the caller records it.
/// </summary>
public sealed class TaxFileStore
{
    private readonly ITaxFileStorage _storage;
    private readonly ITaxFileScanner _scanner;
    private readonly TaxFileOptions _options;

    public TaxFileStore(ITaxFileStorage storage, ITaxFileScanner scanner, IOptions<TaxFileOptions> options)
    {
        _storage = storage;
        _scanner = scanner;
        _options = options.Value;
    }

    public TaxFileStorageState State => _storage.State;
    public long MaxBytes => _options.MaxBytes;
    public bool ScannerConfigured => _scanner.Engine is not null;

    public static string UnavailableMessage(TaxFileStorageState state) => state switch
    {
        TaxFileStorageState.Disabled => "O armazenamento privado de arquivos está desligado nesta instalação.",
        TaxFileStorageState.PrivateBucketMissing => "O armazenamento privado de arquivos não está configurado: nenhum arquivo é enviado ao bucket público.",
        _ => "O armazenamento de arquivos não está disponível.",
    };

    /// <summary>Stores a company's evidence file under its private tax folder.</summary>
    public Task<TaxStoredContent> AcceptTenantFileAsync(Guid tenantId, Stream content, string? fileName, CancellationToken ct)
        => AcceptAsync(ext => StorageKeys.Build(tenantId, "tax/evidence", $"{Guid.NewGuid():N}{ext}", isPrivate: true), content, fileName, ct);

    /// <summary>Stores a platform document (professional opinion, catalogue source).</summary>
    public Task<TaxStoredContent> AcceptPlatformFileAsync(Stream content, string? fileName, CancellationToken ct)
        => AcceptAsync(ext => $"{StorageKeys.PrivatePrefix}platform/tax/documents/{Guid.NewGuid():N}{ext}", content, fileName, ct);

    public Task<Stream> OpenAsync(string key, CancellationToken ct) => _storage.OpenAsync(key, ct);

    private async Task<TaxStoredContent> AcceptAsync(Func<string, string> keyFor, Stream content, string? fileName, CancellationToken ct)
    {
        if (_storage.State != TaxFileStorageState.Available)
            throw new DomainException(UnavailableMessage(_storage.State), "tax_file_storage_unavailable");

        var bytes = await ReadBoundedAsync(content, ct);
        if (bytes.Length == 0) throw new DomainException("O arquivo está vazio.", "tax_file_empty");
        var kind = TaxFileInspector.Detect(bytes)
            ?? throw new DomainException($"Tipo de arquivo não aceito. Envie {string.Join(", ", TaxFileInspector.AcceptedExtensions)}.", "tax_file_type");
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!kind.AcceptedExtensions.Contains(extension))
            throw new DomainException("A extensão do arquivo não corresponde ao seu conteúdo.", "tax_file_extension");
        if (kind.Extension == ".xml" && TaxFileInspector.HasActiveContent(bytes))
            throw new DomainException("O XML contém marcação ativa (HTML, SVG ou script) e não é aceito.", "tax_file_active_content");

        var scan = await _scanner.ScanAsync(bytes, kind.ContentType, ct);
        if (scan == TaxFileScanStatus.Rejected)
            throw new DomainException("O arquivo foi recusado pela verificação de conteúdo.", "tax_file_scan_rejected");
        if (_options.RequireScan && scan != TaxFileScanStatus.Clean)
            throw new DomainException("Esta instalação exige verificação de conteúdo e ela não confirmou o arquivo.", "tax_file_scan_required");

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var key = keyFor(kind.Extension);
        using (var stream = new MemoryStream(bytes, writable: false))
            await _storage.SaveAsync(key, stream, kind.ContentType, bytes.LongLength, ct);
        return new TaxStoredContent(key, TaxFileInspector.SafeName(fileName, kind.Extension), kind.ContentType, kind.Extension, bytes.LongLength,
            sha, scan, _scanner.Engine);
    }

    private async Task<byte[]> ReadBoundedAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > _options.MaxBytes)
                throw new DomainException($"O arquivo passa do limite de {_options.MaxBytes / (1024 * 1024)} MB.", "tax_file_too_large");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
