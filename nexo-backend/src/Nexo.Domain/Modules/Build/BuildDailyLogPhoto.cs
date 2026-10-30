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
    /// <summary>
    /// Durable storage object key. The public URL is composed from this at read time
    /// (a short-lived signed URL, IPrivateFileUrls) — never persisted — so the public domain / CDN /
    /// bucket / provider can change without invalidating existing photos.
    /// </summary>
    public string  StorageKey  { get; private set; } = string.Empty;
    public string? Caption     { get; private set; }
    /// <summary>Shown in the client portal (default false). Only counts when its daily log is client-visible too.</summary>
    public bool    ClientVisible { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildDailyLogPhoto Create(
        Guid    tenantId,
        Guid    dailyLogId,
        string  storageKey,
        string? caption = null)
    {
        if (dailyLogId == Guid.Empty)               throw new DomainException("DailyLogId is required.");
        if (string.IsNullOrWhiteSpace(storageKey))  throw new DomainException("StorageKey is required.");

        return new BuildDailyLogPhoto(tenantId)
        {
            DailyLogId = dailyLogId,
            StorageKey = storageKey.Trim(),
            Caption    = caption?.Trim(),
        };
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void UpdateCaption(string? caption)
    {
        Caption = caption?.Trim();
        SetUpdatedAt();
    }

    public void SetClientVisible(bool visible)
    {
        ClientVisible = visible;
        SetUpdatedAt();
    }
}
