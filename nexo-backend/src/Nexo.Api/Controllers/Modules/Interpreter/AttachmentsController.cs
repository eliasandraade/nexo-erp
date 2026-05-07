using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Api.Controllers.Modules.Interpreter;

/// <summary>
/// Staged file upload for movement attachments (receipts, invoices, etc.).
/// File is stored before the movement exists — association happens during Analyze.
/// </summary>
[ApiController]
[Route("api/v1/interpreter/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/jpg",
        "image/png",
    };

    // Magic bytes per format — validated before accepting upload.
    private static readonly byte[] PdfMagic  = "%PDF"u8.ToArray();
    private static readonly byte[] JpegMagic = new byte[] { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngMagic  = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int  MagicReadBytes = 8;

    private readonly IAttachmentStorage           _storage;
    private readonly IMovementAttachmentRepository _attachmentRepo;
    private readonly ICurrentTenant               _currentTenant;

    public AttachmentsController(
        IAttachmentStorage            storage,
        IMovementAttachmentRepository attachmentRepo,
        ICurrentTenant                currentTenant)
    {
        _storage        = storage;
        _attachmentRepo = attachmentRepo;
        _currentTenant  = currentTenant;
    }

    /// <summary>
    /// Upload a receipt/invoice before analysis.
    /// Returns an attachmentId that can be passed to POST /interpreter/analyze.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxSizeBytes + 1024)]
    public async Task<ActionResult<AttachmentUploadResponse>> Upload(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (file.Length > MaxSizeBytes)
            return BadRequest(new { error = "File exceeds maximum size of 10 MB." });

        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(new { error = "Only PDF, JPG, and PNG files are accepted." });

        // Validate magic bytes — prevents content-type spoofing.
        var header = new byte[MagicReadBytes];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, MagicReadBytes), ct);
        stream.Seek(0, SeekOrigin.Begin);

        if (!IsValidMagicBytes(file.ContentType, header.AsSpan(0, read)))
            return BadRequest(new { error = "File content does not match declared content type." });

        // Sanitize filename — strip path traversal characters and null bytes.
        var safeFileName = SanitizeFileName(file.FileName);
        if (safeFileName.Length == 0)
            return BadRequest(new { error = "Invalid file name." });

        var tenantId = _currentTenant.Id;

        var storageKey = await _storage.UploadAsync(
            stream, safeFileName, file.ContentType, tenantId, ct);

        var attachment = MovementAttachment.CreatePending(
            tenantId:    tenantId,
            fileName:    safeFileName,
            contentType: file.ContentType,
            storageKey:  storageKey,
            sizeBytes:   file.Length);

        await _attachmentRepo.AddAsync(attachment, ct);
        await _attachmentRepo.SaveChangesAsync(ct);

        return Ok(new AttachmentUploadResponse(
            AttachmentId: attachment.Id,
            FileName:     attachment.FileName,
            SizeBytes:    attachment.SizeBytes));
    }

    private static bool IsValidMagicBytes(string contentType, ReadOnlySpan<byte> header)
    {
        if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return header.Length >= PdfMagic.Length && header[..PdfMagic.Length].SequenceEqual(PdfMagic);

        if (contentType.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("image/jpg",  StringComparison.OrdinalIgnoreCase))
            return header.Length >= JpegMagic.Length && header[..JpegMagic.Length].SequenceEqual(JpegMagic);

        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            return header.Length >= PngMagic.Length && header[..PngMagic.Length].SequenceEqual(PngMagic);

        return false;
    }

    private static string SanitizeFileName(string rawName)
    {
        // Take only the base name (no directory components) and strip dangerous chars.
        var baseName = Path.GetFileName(rawName ?? string.Empty);
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '\0', '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            .ToHashSet();

        var sanitized = new string(baseName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return sanitized.Length > 0 ? sanitized : string.Empty;
    }
}

// ── Response ──────────────────────────────────────────────────────────────────

public record AttachmentUploadResponse(
    Guid   AttachmentId,
    string FileName,
    long   SizeBytes);
