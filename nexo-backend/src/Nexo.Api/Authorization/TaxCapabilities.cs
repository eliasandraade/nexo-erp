namespace Nexo.Api.Authorization;

/// <summary>
/// Capabilities of Tax Intelligence mapped onto the role sets (docs/security/rbac-matrix.md, "Inteligência tributária"). The company
/// side never reaches the platform side: platform tokens carry no tenant and company tokens never pass the Platform policies.
/// </summary>
public static class TaxCapabilities
{
    /// <summary>Read the module: overview, calculations, simulations, opportunities, credits, split, rules, sources.</summary>
    public const string View = AppRoles.Management;
    /// <summary>Calculate operations and run simulations / supplier comparisons.</summary>
    public const string Simulate = AppRoles.Management;
    /// <summary>Analyse opportunities: open the review, answer requirements, add/replace/revoke evidence, assign.</summary>
    public const string Analyze = AppRoles.Management;
    /// <summary>Approve a scenario, verify the execution, block a definitive decision — the board only.</summary>
    public const string Decide = AppRoles.Directors;
    /// <summary>Ledger movements of credits (appropriate, use, reverse, expire, block, unblock) — the board only.</summary>
    public const string CreditLedger = AppRoles.Directors;
    /// <summary>Register potential credits and move them through the requirements review.</summary>
    public const string CreditReview = AppRoles.Management;
    /// <summary>Record and reverse split retentions.</summary>
    public const string Split = AppRoles.Management;
    /// <summary>Company policies (materiality, retention), tax profile versions, compliance assessments, legal holds.</summary>
    public const string Policies = AppRoles.Directors;
    /// <summary>Export dossiers (and their evidence files) and download revoked files for audit.</summary>
    public const string Export = AppRoles.Directors;
    /// <summary>Classify the company's products (item kind, NCM/NBS, cClassTrib).</summary>
    public const string Classify = AppRoles.Management;

    /// <summary>Platform operators (super_admin and support): read everything, and suspend the module in an incident.</summary>
    public const string PlatformRead = "Platform";
    /// <summary>Platform administrators (super_admin): activate, approve, record reviews, import catalogues, configure the pilot.</summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
