using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// The company as the client portal shows it: its public slug (first segment of <c>/{company}/{project}</c>,
/// unique across the platform), display name, logo and contacts. One per tenant.
/// </summary>
public class BuildPortalCompany : TenantEntity
{
    private BuildPortalCompany() { }
    private BuildPortalCompany(Guid tenantId) : base(tenantId) { }

    public string  Slug         { get; private set; } = string.Empty;
    public string  DisplayName  { get; private set; } = string.Empty;
    /// <summary>Public storage key (context build-portal-logo) of this tenant.</summary>
    public string? LogoKey      { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }

    public static BuildPortalCompany Create(Guid tenantId, string slug, string displayName,
        string? logoKey, string? contactEmail, string? contactPhone)
    {
        var c = new BuildPortalCompany(tenantId);
        c.Update(slug, displayName, logoKey, contactEmail, contactPhone);
        return c;
    }

    public void Update(string slug, string displayName, string? logoKey, string? contactEmail, string? contactPhone)
    {
        var s = BuildPortalSlug.Normalize(slug);
        var problem = BuildPortalSlug.Validate(s);
        if (problem is not null) throw new DomainException(problem, "portal_slug_invalid");
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("Informe o nome da empresa no portal.");
        if (displayName.Trim().Length > 200) throw new DomainException("O nome pode ter até 200 caracteres.");

        Slug         = s;
        DisplayName  = displayName.Trim();
        LogoKey      = BuildPortalText.Clean(logoKey, 500, "logo");
        ContactEmail = BuildPortalText.Clean(contactEmail, 200, "e-mail");
        ContactPhone = BuildPortalText.Clean(contactPhone, 40, "telefone");
        SetUpdatedAt();
    }
}

/// <summary>Small text helpers shared by the portal entities.</summary>
public static class BuildPortalText
{
    public static string? Clean(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > max) throw new DomainException($"O campo {field} pode ter até {max} caracteres.");
        return v;
    }

    /// <summary>True for a #rrggbb hex string. Non-regex on purpose (avoids a regex security hotspot).</summary>
    public static bool IsHexColor(string? s) =>
        s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}
