using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// A provision of law a rule rests on (platform-wide, no tenant). The excerpt is the literal text
/// of the official source, so a decision can be explained years later even if the page changes.
/// The text of a verified source never changes: a new wording of the law is a new source.
/// </summary>
public class TaxLegalSource : BaseEntity
{
    private TaxLegalSource() { }   // EF Core

    /// <summary>Stable code, e.g. "LC214-2025-ART12-P2-III". Unique.</summary>
    public string Code { get; private set; } = string.Empty;
    public LegalSourceKind Kind { get; private set; }
    /// <summary>Act, e.g. "Lei Complementar nº 214, de 16 de janeiro de 2025".</summary>
    public string Title { get; private set; } = string.Empty;
    /// <summary>Provision inside the act, e.g. "art. 12, § 2º, III".</summary>
    public string Reference { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public string Excerpt { get; private set; } = string.Empty;
    public DateOnly? PublishedOn { get; private set; }
    public LegalSourceVerification Verification { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public string? VerifiedBy { get; private set; }
    public string? Notes { get; private set; }

    public bool IsVerified => Verification == LegalSourceVerification.Verified;

    public static TaxLegalSource Create(
        string code, LegalSourceKind kind, string title, string reference, string url, string excerpt,
        DateOnly? publishedOn, string? notes)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Informe o código da fonte.");
        if (string.IsNullOrWhiteSpace(excerpt)) throw new DomainException("Informe o texto literal da fonte.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new DomainException("A fonte precisa de um endereço https oficial.");

        return new TaxLegalSource
        {
            Code = code.Trim().ToUpperInvariant(),
            Kind = kind,
            Title = title.Trim(),
            Reference = reference.Trim(),
            Url = url.Trim(),
            Excerpt = excerpt.Trim(),
            PublishedOn = publishedOn,
            Verification = LegalSourceVerification.Unverified,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }

    /// <summary>Records that someone compared the excerpt with the official text.</summary>
    public void MarkVerified(string verifiedBy, DateTime verifiedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(verifiedBy)) throw new DomainException("Informe quem conferiu a fonte.");
        Verification = LegalSourceVerification.Verified;
        VerifiedBy = verifiedBy.Trim();
        VerifiedAt = verifiedAtUtc;
        SetUpdatedAt();
    }
}
