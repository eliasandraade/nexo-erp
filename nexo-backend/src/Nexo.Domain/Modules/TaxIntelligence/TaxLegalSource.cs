using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// A provision of law a rule rests on (platform-wide, no tenant). The excerpt is the literal text
/// of the official source, so a decision can be explained years later even if the page changes.
/// The text of a verified source never changes: a new wording of the law is a new source.
/// "Verified" means the excerpt matches the official text — never that a rule built on it was
/// professionally approved (that is <see cref="TaxRuleVersion.ProfessionalReview"/>).
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
    public TextVerificationStatus TextVerification { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public string? VerifiedBy { get; private set; }
    public string? Notes { get; private set; }

    public bool IsTextVerified => TextVerification == TextVerificationStatus.Verified;

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
            TextVerification = TextVerificationStatus.Unverified,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }

    /// <summary>The authenticated user who recorded the verification (kept apart from who compared, as informed).</summary>
    public Guid? VerificationRecordedById { get; private set; }
    public string? VerificationRecordedBy { get; private set; }

    /// <summary>
    /// Records that someone compared the excerpt with the official text (text only, not a legal opinion). Who
    /// compared is as informed; who recorded it is the authenticated user. A verified source activates nothing.
    /// </summary>
    public void MarkVerified(string verifiedBy, DateTime verifiedAtUtc, Guid? recordedById = null, string? recordedBy = null)
    {
        if (string.IsNullOrWhiteSpace(verifiedBy)) throw new DomainException("Informe quem conferiu a fonte.");
        if (IsTextVerified) throw new DomainException("O texto desta fonte já foi conferido; um novo texto é uma nova fonte.", "tax_legal_source_verified");
        TextVerification = TextVerificationStatus.Verified;
        VerifiedBy = verifiedBy.Trim();
        VerifiedAt = verifiedAtUtc;
        VerificationRecordedById = recordedById;
        VerificationRecordedBy = string.IsNullOrWhiteSpace(recordedBy) ? null : recordedBy.Trim();
        SetUpdatedAt();
    }
}
