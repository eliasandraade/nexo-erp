using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

public class MovementAttachment : TenantEntity
{
    private MovementAttachment() { }
    private MovementAttachment(Guid tenantId) : base(tenantId) { }

    public Guid   MovementId   { get; private set; }
    public string FileName     { get; private set; } = string.Empty;
    public string ContentType  { get; private set; } = string.Empty;
    public string StorageKey   { get; private set; } = string.Empty;
    public long   SizeBytes    { get; private set; }

    public static MovementAttachment Create(
        Guid   tenantId,
        Guid   movementId,
        string fileName,
        string contentType,
        string storageKey,
        long   sizeBytes)
    {
        if (movementId == Guid.Empty)
            throw new DomainException("MovementId is required.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new DomainException("FileName is required.");
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new DomainException("StorageKey is required.");
        if (sizeBytes <= 0)
            throw new DomainException("SizeBytes must be positive.");

        return new MovementAttachment(tenantId)
        {
            MovementId  = movementId,
            FileName    = fileName.Trim(),
            ContentType = contentType.Trim(),
            StorageKey  = storageKey,
            SizeBytes   = sizeBytes
        };
    }

    // Staged upload: file is stored before the movement exists.
    // MovementId = Guid.Empty until the movement is created during Analyze.
    // No DB-level FK on movement_id — intentional, allows staging.
    public static MovementAttachment CreatePending(
        Guid   tenantId,
        string fileName,
        string contentType,
        string storageKey,
        long   sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new DomainException("FileName is required.");
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new DomainException("StorageKey is required.");
        if (sizeBytes <= 0)
            throw new DomainException("SizeBytes must be positive.");

        return new MovementAttachment(tenantId)
        {
            MovementId  = Guid.Empty,
            FileName    = fileName.Trim(),
            ContentType = contentType.Trim(),
            StorageKey  = storageKey,
            SizeBytes   = sizeBytes
        };
    }
}
