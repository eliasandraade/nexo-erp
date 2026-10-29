using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

/// <summary>
/// What a calculation must say about the ancillary-obligation assessment in force on its date — so a
/// declared, remediated or PNCT-related fact is never shown as if it had been verified. Texts describe
/// the record only; they draw no tax conclusion and never say "the company is compliant".
/// </summary>
public static class TaxComplianceAdvisories
{
    public const string NoAssessment =
        "Nenhuma avaliação das obrigações acessórias do IBS/CBS cobre a data: nenhuma conclusão sobre elas foi aplicada; " +
        "regras que dependem do cumprimento verificado não foram aplicadas.";

    public const string Declared =
        "Obrigações acessórias declaradas, não verificadas: a declaração não equivale a verificação, e regras que dependem " +
        "do cumprimento verificado não foram aplicadas. Quais documentos comprovam o cumprimento ainda depende de revisão profissional.";

    public const string Remediation =
        "Inconsistência identificada ou em regularização: não equivale a obrigações verificadas; regras que dependem " +
        "do cumprimento verificado não foram aplicadas.";

    public const string RemediationVerified =
        "Regularização verificada: não equivale automaticamente a obrigações verificadas no período; regras que dependem " +
        "do cumprimento verificado não foram aplicadas até revisão profissional.";

    public const string NonCompliant =
        "Obrigações acessórias registradas como não cumpridas no período: regras que dependem do cumprimento não se aplicam.";

    public const string NotApplicable =
        "Avaliação registrada como não aplicável: nenhuma conclusão sobre obrigações acessórias foi aplicada.";

    public const string Pnct =
        "Participação no Programa Nacional de Conformidade Tributária (Ato Conjunto RFB/CGIBS nº 5/2026) registrada: " +
        "não significa regularidade integral nem gera benefício automático.";

    public static string Verified(TaxComplianceAssessment a)
        => $"Obrigações verificadas no período e no escopo indicados: {a.PeriodFrom:dd/MM/yyyy} a {a.PeriodTo:dd/MM/yyyy}, {a.Scope}" +
           $" (avaliado por {a.AssessedBy}){(a.Limitations is null ? "" : $"; limitações: {a.Limitations}")}.";

    public static IReadOnlyList<string> For(TaxComplianceAssessment? a)
    {
        if (a is null) return [NoAssessment];
        var main = a.Status switch
        {
            ComplianceAssessmentStatus.Verified => Verified(a),
            ComplianceAssessmentStatus.Declared => Declared,
            ComplianceAssessmentStatus.InconsistencyIdentified or ComplianceAssessmentStatus.UnderRemediation => Remediation,
            ComplianceAssessmentStatus.RemediationVerified => RemediationVerified,
            ComplianceAssessmentStatus.NonCompliant => NonCompliant,
            ComplianceAssessmentStatus.NotApplicable => NotApplicable,
            _ => NoAssessment,
        };
        return a.PnctParticipation ? [main, Pnct] : [main];
    }
}
