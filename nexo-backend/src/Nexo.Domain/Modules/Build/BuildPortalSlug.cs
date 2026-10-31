namespace Nexo.Domain.Modules.Build;

/// <summary>
/// Slugs of the client portal (company and project): the public address is <c>/{company}/{project}</c>.
/// Rules: 3–60 characters, lowercase a–z, digits and '-', no '-' at the ends nor doubled, and not one of
/// the reserved words (every first path segment of the Orken app router plus infrastructure names), so a
/// portal address never shadows an app route. Checked without regular expressions on purpose.
/// </summary>
public static class BuildPortalSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 60;

    /// <summary>
    /// First path segments of nexo-main/src/app/router/AppRouter.tsx (keep in sync) plus names reserved
    /// for infrastructure and future use.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        // app routes
        "acesso-encerrado", "agendar", "assinatura", "auditoria", "build", "caixa", "check-email", "clientes",
        "compras", "configuracoes", "dashboard", "esqueci-senha", "estoque", "financeiro", "fiscal",
        "forgot-password", "fornecedores", "impersonate", "listas-de-preco", "login", "menu", "pdv", "perfil",
        "platform", "produtos", "rastrear", "redefinir-senha", "register", "relatorios", "restaurante",
        "service", "tributos", "usuarios", "vendas", "verify-email", "workspaces",
        // infrastructure / reserved
        "api", "app", "admin", "assets", "static", "www", "orken", "portal", "obra", "obras", "health",
        "public", "logout", "reset-password",
    };

    /// <summary>Lowercases and trims a typed slug (does not otherwise change it).</summary>
    public static string Normalize(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Null when valid, otherwise the reason in Portuguese.</summary>
    public static string? Validate(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return "Informe o endereço (slug).";
        if (slug.Length < MinLength || slug.Length > MaxLength)
            return $"O endereço precisa ter entre {MinLength} e {MaxLength} caracteres.";
        foreach (var c in slug)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
            if (!ok) return "Use só letras minúsculas sem acento, números e hífen.";
        }
        if (slug[0] == '-' || slug[^1] == '-') return "O endereço não pode começar nem terminar com hífen.";
        if (slug.Contains("--", StringComparison.Ordinal)) return "O endereço não pode ter dois hífens seguidos.";
        if (Reserved.Contains(slug)) return "Este endereço é reservado. Escolha outro.";
        return null;
    }

    public static bool IsValid(string? slug) => Validate(slug) is null;
}
