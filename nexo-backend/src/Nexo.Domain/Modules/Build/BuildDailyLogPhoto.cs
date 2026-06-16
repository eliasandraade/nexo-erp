using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// Photo attached to a BuildDailyLog.
/// StorageKey is the blob/filesystem key (same pattern as MovementAttachment).
/// Caption is optional free text.
/// </summary>
public class BuildDailyLogPhoto : TenantEntity
{
    private BuildDailyLogPhoto() { }
    private BuildDailyLogPhoto(Guid tenantId) : base(tenantId) { }

    public Guid    DailyLogId  { get; private set; }
    public string  StorageKey  { get; private set; } = string.Empty;
    /// <summary>Public URL returned by the storage upload (same pattern as Product.ImageUrl). Null until storage is configured.</summary>
    public string? Url         { get; private set; }
    public string? Caption     { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildDailyLogPhoto Create(
        Guid    tenantId,
        Guid    dailyLogId,
        string  storageKey,
        string? url     = null,
        string? caption = null)
    {
        if (dailyLogId == Guid.Empty)               throw new DomainException("DailyLogId is required.");
        if (string.IsNullOrWhiteSpace(storageKey))  throw new DomainException("StorageKey is required.");

        return new BuildDailyLogPhoto(tenantId)
        {
            DailyLogId = dailyLogId,
            StorageKey = storageKey.Trim(),
            Url        = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            Caption    = caption?.Trim(),
        };
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void UpdateCaption(string? caption)
    {
        Caption = caption?.Trim();
        SetUpdatedAt();
    }
}
