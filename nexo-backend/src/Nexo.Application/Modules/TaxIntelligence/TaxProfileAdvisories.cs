using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

/// <summary>
/// What a calculation must say about the facts of the profile it used — so a declared fact is never
/// shown as if it had been verified. Texts only describe the data; they draw no tax conclusion.
/// </summary>
public static class TaxProfileAdvisories
{
    public const string AccessoryObligationsUnknown =
        "Cumprimento das obrigações acessórias do IBS/CBS não informado: nenhuma conclusão sobre ele foi aplicada; " +
        "regras que dependem desse cumprimento não foram aplicadas.";

    public const string AccessoryObligationsDeclared =
        "Obrigações acessórias declaradas como cumpridas, mas não verificadas: a declaração não equivale a verificação, " +
        "e regras que dependem do cumprimento verificado não foram aplicadas. Quais documentos comprovam o cumprimento " +
        "ainda depende de revisão profissional.";

    public const string AccessoryObligationsNonCompliant =
        "Obrigações acessórias registradas como não cumpridas: regras que dependem do cumprimento não se aplicam.";

    public static IReadOnlyList<string> For(AccessoryObligationsStatus accessoryObligations) => accessoryObligations switch
    {
        AccessoryObligationsStatus.Unknown => [AccessoryObligationsUnknown],
        AccessoryObligationsStatus.DeclaredCompliant => [AccessoryObligationsDeclared],
        AccessoryObligationsStatus.NonCompliant => [AccessoryObligationsNonCompliant],
        _ => [],
    };
}
