using System.Text.Json;
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

public enum BuildPortalAccess
{
    Public   = 1,
    Password = 2,
}

/// <summary>
/// The client portal of one project: its slug (second segment of <c>/{company}/{project}</c>, unique per
/// tenant), whether it is published, how it is opened (public or with a password, kept only as a BCrypt
/// hash), branding, contacts and what it shows (<see cref="BuildPortalVisibility"/>).
///
/// Password rules: changing or removing the password bumps <see cref="PasswordVersion"/>, which
/// invalidates every open session. Wrong passwords lock in two layers (both for <see cref="LockMinutes"/> minutes):
/// <see cref="MaxFailedAttemptsPerClient"/> from one client IP lock that IP only (kept in the cache, so a guesser cannot
/// lock the real client out), and <see cref="MaxFailedAttemptsGlobal"/> from anywhere lock the whole portal
/// (<see cref="FailedAttempts"/>/<see cref="LockedUntil"/>, counted atomically in the database) to bound a distributed attack.
/// </summary>
public class BuildProjectPortal : TenantEntity
{
    public const int MaxFailedAttemptsPerClient = 20;
    public const int MaxFailedAttemptsGlobal    = 200;
    public const int LockMinutes                = 15;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private BuildProjectPortal() { }
    private BuildProjectPortal(Guid tenantId) : base(tenantId) { }

    public Guid              ProjectId       { get; private set; }
    public string?           Slug            { get; private set; }
    public bool              IsPublished     { get; private set; }
    public DateTime?         PublishedAt     { get; private set; }
    public BuildPortalAccess Access          { get; private set; } = BuildPortalAccess.Public;
    public string?           PasswordHash    { get; private set; }
    public int               PasswordVersion { get; private set; }
    public int               FailedAttempts  { get; private set; }
    public DateTime?         LockedUntil     { get; private set; }
    public string?           Title           { get; private set; }
    public string?           WelcomeMessage  { get; private set; }
    public string?           LogoKey         { get; private set; }
    public string?           CoverKey        { get; private set; }
    public string?           PrimaryColor    { get; private set; }
    public string?           AccentColor     { get; private set; }
    public string?           ContactName     { get; private set; }
    public string?           ContactPhone    { get; private set; }
    public string?           ContactEmail    { get; private set; }
    public string?           ContactWhatsApp { get; private set; }
    public string            VisibilityJson  { get; private set; } = "{}";

    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);

    public BuildPortalVisibility Visibility
        => string.IsNullOrWhiteSpace(VisibilityJson)
            ? BuildPortalVisibility.Defaults
            : JsonSerializer.Deserialize<BuildPortalVisibility>(VisibilityJson, Json) ?? BuildPortalVisibility.Defaults;

    public static BuildProjectPortal Create(Guid tenantId, Guid projectId)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        return new BuildProjectPortal(tenantId)
        {
            ProjectId      = projectId,
            VisibilityJson = JsonSerializer.Serialize(BuildPortalVisibility.Defaults, Json),
        };
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public void Configure(
        string? slug, BuildPortalAccess access, string? title, string? welcomeMessage,
        string? logoKey, string? coverKey, string? primaryColor, string? accentColor,
        string? contactName, string? contactPhone, string? contactEmail, string? contactWhatsApp,
        BuildPortalVisibility visibility)
    {
        string? cleanSlug = null;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            cleanSlug = BuildPortalSlug.Normalize(slug);
            var problem = BuildPortalSlug.Validate(cleanSlug);
            if (problem is not null) throw new DomainException(problem, "portal_slug_invalid");
        }
        if (IsPublished && cleanSlug is null)
            throw new DomainException("Um portal publicado precisa de endereço. Despublique antes de removê-lo.");
        if (!Enum.IsDefined(access)) throw new DomainException("Tipo de acesso inválido.");
        if (IsPublished && access == BuildPortalAccess.Password && !HasPassword)
            throw new DomainException("Defina a senha do portal antes de exigir senha.", "portal_password_missing");
        foreach (var color in new[] { primaryColor, accentColor })
            if (!string.IsNullOrWhiteSpace(color) && !BuildPortalText.IsHexColor(color.Trim()))
                throw new DomainException("Cor inválida: use o formato #RRGGBB.");

        Slug            = cleanSlug;
        Access          = access;
        Title           = BuildPortalText.Clean(title, 200, "título");
        WelcomeMessage  = BuildPortalText.Clean(welcomeMessage, 2000, "mensagem de boas-vindas");
        LogoKey         = BuildPortalText.Clean(logoKey, 500, "logo");
        CoverKey        = BuildPortalText.Clean(coverKey, 500, "capa");
        PrimaryColor    = string.IsNullOrWhiteSpace(primaryColor) ? null : primaryColor.Trim().ToLowerInvariant();
        AccentColor     = string.IsNullOrWhiteSpace(accentColor) ? null : accentColor.Trim().ToLowerInvariant();
        ContactName     = BuildPortalText.Clean(contactName, 200, "contato");
        ContactPhone    = BuildPortalText.Clean(contactPhone, 40, "telefone");
        ContactEmail    = BuildPortalText.Clean(contactEmail, 200, "e-mail");
        ContactWhatsApp = BuildPortalText.Clean(contactWhatsApp, 40, "WhatsApp");
        VisibilityJson  = JsonSerializer.Serialize(visibility ?? BuildPortalVisibility.Defaults, Json);
        SetUpdatedAt();
    }

    // ── Password ──────────────────────────────────────────────────────────────

    /// <summary>Stores a new password hash; every session opened with the old one stops working.</summary>
    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Senha inválida.");
        PasswordHash    = passwordHash;
        PasswordVersion++;
        FailedAttempts  = 0;
        LockedUntil     = null;
        SetUpdatedAt();
    }

    /// <summary>Removes the password (and every session). Refused while a published portal requires it.</summary>
    public void ClearPassword()
    {
        if (IsPublished && Access == BuildPortalAccess.Password)
            throw new DomainException(
                "Este portal publicado exige senha. Mude o acesso para público ou despublique antes de remover a senha.",
                "portal_password_required_by_access");
        PasswordHash    = null;
        PasswordVersion++;
        FailedAttempts  = 0;
        LockedUntil     = null;
        SetUpdatedAt();
    }

    /// <summary>The whole portal is locked (global threshold). The counting itself is one atomic SQL update in the service.</summary>
    public bool IsLocked(DateTime nowUtc) => LockedUntil is { } until && until > nowUtc;

    // ── Publication ───────────────────────────────────────────────────────────

    /// <summary>Publishes the portal. Needs a slug, and a password when access is by password.</summary>
    public void Publish(DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(Slug))
            throw new DomainException("Defina o endereço do portal da obra antes de publicar.", "portal_slug_missing");
        if (Access == BuildPortalAccess.Password && !HasPassword)
            throw new DomainException("Defina a senha do portal antes de publicar com acesso por senha.", "portal_password_missing");
        IsPublished = true;
        PublishedAt ??= nowUtc;
        SetUpdatedAt();
    }

    public void Unpublish()
    {
        IsPublished = false;
        SetUpdatedAt();
    }
}
