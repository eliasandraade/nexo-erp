using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Integrations.Storage;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Project documents (contract, design, ART/RRT, permit, invoice, report, manual, warranty, photo, other) — company side.
/// Stored privately under <c>private/tenants/{t}/build/documents/{project}/{id}.&lt;ext&gt;</c> (upload context
/// <c>build-document</c>): PDF, JPEG, PNG or WebP checked by their first bytes, ≤ 20 MB, SHA-256 kept; the client's file name
/// is display only. Hidden from the client portal until marked client-visible; the AI extraction stays internal.
/// </summary>
public sealed class BuildDocumentService
{
    public const string Folder = "build/documents";
    public const string Context = "build-document";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly IStorageProvider _storage;
    private readonly IPrivateFileUrls _urls;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly ILogger<BuildDocumentService> _logger;

    public BuildDocumentService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, IStorageProvider storage, IPrivateFileUrls urls,
        IIntegrationFeatureFlags flags, ILogger<BuildDocumentService> logger)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _storage = storage;
        _urls = urls;
        _flags = flags;
        _logger = logger;
    }

    private Guid Tid => _tenant.Id;

    /// <summary>The accepted content kinds: (content type, extension).</summary>
    public static (string ContentType, string Extension)? KindOf(BuildFileKind kind) => kind switch
    {
        BuildFileKind.Pdf => ("application/pdf", ".pdf"),
        BuildFileKind.Jpeg => ("image/jpeg", ".jpg"),
        BuildFileKind.Png => ("image/png", ".png"),
        BuildFileKind.Webp => ("image/webp", ".webp"),
        _ => null,
    };

    public async Task<BuildDocumentDto> CreateAsync(Guid projectId, string? category, string? title, DateOnly? validUntil, bool clientVisible,
        BuildUploadBuffer file, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var kind = KindOf(BuildFileSignatures.Sniff(file.Head))
                   ?? throw new BuildModelFileException("Envie um PDF, JPEG, PNG ou WebP (o conteúdo do arquivo é conferido).", "invalid_file_content");
        if (file.Length > BuildDocument.MaxBytes) throw new BuildFileTooLargeException("Documento grande demais: o limite é 20 MB.");
        var cat = BuildScope.ParseEnum(category, BuildDocumentCategory.Outro, "Categoria do documento");
        var cleanTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(BuildDocument.SanitizeFileName(file.FileName)) : title;

        if (!_flags.StorageEnabled) throw new BuildStorageUnavailableException("Armazenamento de arquivos indisponível.");
        var id = Guid.NewGuid();
        var key = StorageKeys.Build(Tid, $"{Folder}/{projectId}", id.ToString("N") + kind.Extension, isPrivate: true);
        var doc = BuildDocument.Create(Tid, projectId, id, cat, cleanTitle, key, file.FileName, kind.ContentType, file.Length, file.Sha256,
            _audit.UserId, _audit.UserName);
        doc.Describe(cat, doc.Title, validUntil);
        doc.SetClientVisible(clientVisible);
        try
        {
            await using var stream = file.Open();
            await _storage.UploadAsync(new StorageUploadRequest(stream, "documento" + kind.Extension, kind.ContentType, key, file.Length), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Build] Document upload failed — key={Key}", key);
            throw new BuildStorageUnavailableException("Serviço de armazenamento indisponível. Tente novamente.", ex);
        }
        _db.BldDocuments.Add(doc);
        _audit.Record(AuditActions.BuildDocumentSaved, "Document", doc.Id, $"Documento enviado ({cat}): {doc.Title}.",
            new { projectId, category = cat.ToString(), doc.SizeBytes, doc.ClientVisible });
        await _db.SaveBuildAsync(ct);
        return Map(doc);
    }

    public async Task<IReadOnlyList<BuildDocumentDto>> ListAsync(Guid projectId, string? category, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var q = _db.BldDocuments.AsNoTracking().Where(d => d.TenantId == Tid && d.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = BuildScope.ParseEnum(category, BuildDocumentCategory.Outro, "Categoria do documento");
            q = q.Where(d => d.Category == c);
        }
        return (await q.OrderByDescending(d => d.CreatedAt).Take(500).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<BuildDocumentDto> GetAsync(Guid id, CancellationToken ct) => Map(await LoadAsync(id, tracking: false, ct));

    public async Task<BuildDocumentDto> UpdateAsync(Guid id, UpdateBuildDocumentRequest r, CancellationToken ct)
    {
        var d = await LoadAsync(id, tracking: true, ct);
        d.Describe(BuildScope.ParseEnum(r.Category, d.Category, "Categoria do documento"), r.Title ?? d.Title, r.ValidUntil);
        _audit.Record(AuditActions.BuildDocumentSaved, "Document", d.Id, $"Documento alterado: {d.Title}.");
        await _db.SaveBuildAsync(ct);
        return Map(d);
    }

    public async Task<BuildDocumentDto> SetVisibilityAsync(Guid id, bool visible, CancellationToken ct)
    {
        var d = await LoadAsync(id, tracking: true, ct);
        d.SetClientVisible(visible);
        _audit.Record(AuditActions.BuildDocumentVisibility, "Document", d.Id,
            visible ? $"Documento visível ao cliente: {d.Title}." : $"Documento ocultado do cliente: {d.Title}.");
        await _db.SaveBuildAsync(ct);
        return Map(d);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var d = await LoadAsync(id, tracking: true, ct);
        _db.BldDocuments.Remove(d);
        _audit.Record(AuditActions.BuildDocumentDeleted, "Document", d.Id, $"Documento excluído: {d.Title}.");
        await _db.SaveBuildAsync(ct);
        if (_flags.StorageEnabled)
        {
            try { await _storage.DeleteAsync(d.FileKey, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "[Build] Could not delete document file"); }
        }
    }

    private async Task<BuildDocument> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var q = _db.BldDocuments.Where(d => d.TenantId == Tid && d.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("BuildDocument", id);
    }

    private BuildDocumentDto Map(BuildDocument d)
    {
        var url = _urls.For(d.FileKey);
        JsonElement? extracted = null;
        if (d.ExtractedJson is { } json)
        {
            using var doc = JsonDocument.Parse(json);
            extracted = doc.RootElement.Clone();
        }
        return new BuildDocumentDto(d.Id, d.ProjectId, d.Category.ToString(), d.Title, d.FileName, d.ContentType, d.SizeBytes, d.Sha256,
            d.ClientVisible, d.ValidUntil, extracted, d.SuggestionId, d.UploadedByName, url,
            url is null ? null : DateTimeOffset.UtcNow.Add(PrivateFileUrls.Ttl), BuildScope.Utc(d.CreatedAt));
    }
}
