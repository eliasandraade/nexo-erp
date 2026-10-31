using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// CLIENT PORTAL — internal (company) side. Never carries the password or its hash.
// ═══════════════════════════════════════════════════════════════════════

public record BuildPortalCompanyDto(
    bool    Configured,
    string? Slug,
    string? DisplayName,
    string? LogoKey,
    string? LogoUrl,
    string? ContactEmail,
    string? ContactPhone);

public record UpdateBuildPortalCompanyRequest(
    string  Slug,
    string  DisplayName,
    string? LogoKey      = null,
    string? ContactEmail = null,
    string? ContactPhone = null);

/// <param name="PublicPath">"/{companySlug}/{projectSlug}" when both slugs exist (the external portal's address).</param>
public record BuildProjectPortalDto(
    Guid                  ProjectId,
    bool                  Exists,
    string?               Slug,
    string?               CompanySlug,
    string?               PublicPath,
    bool                  IsPublished,
    DateTimeOffset?       PublishedAt,
    string                Access,
    bool                  HasPassword,
    bool                  IsLocked,
    string?               Title,
    string?               WelcomeMessage,
    string?               LogoKey,
    string?               LogoUrl,
    string?               CoverKey,
    string?               CoverUrl,
    string?               PrimaryColor,
    string?               AccentColor,
    string?               ContactName,
    string?               ContactPhone,
    string?               ContactEmail,
    string?               ContactWhatsApp,
    BuildPortalVisibility Visibility);

public record UpdateBuildProjectPortalRequest(
    string?                Slug            = null,
    string?                Access          = null,   // "Public" | "Password" (default Public)
    string?                Title           = null,
    string?                WelcomeMessage  = null,
    string?                LogoKey         = null,
    string?                CoverKey        = null,
    string?                PrimaryColor    = null,
    string?                AccentColor     = null,
    string?                ContactName     = null,
    string?                ContactPhone    = null,
    string?                ContactEmail    = null,
    string?                ContactWhatsApp = null,
    BuildPortalVisibility? Visibility      = null);  // null = safe defaults

public record SetBuildPortalPasswordRequest(string Password);

public record SetDailyLogClientVisibilityRequest(bool ClientVisible, string? ClientSummary = null);

public record SetPhotoClientVisibilityRequest(bool ClientVisible);

public record BuildClientUpdateDto(
    Guid            Id,
    Guid            ProjectId,
    string          Kind,
    string          Title,
    string          Body,
    string          Status,
    DateTimeOffset? PublishedAt,
    string          Source,
    string          CreatedByName,
    DateTimeOffset  CreatedAt,
    DateTimeOffset  UpdatedAt);

public record SaveBuildClientUpdateRequest(string Title, string Body, string? Kind = null);
