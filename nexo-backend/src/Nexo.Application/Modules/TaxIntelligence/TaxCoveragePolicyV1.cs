using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

/// <summary>A reason an operation is (or is not) inside the recorte engine V1 supports.</summary>
public sealed record TaxCoverageFinding(TaxCoverageStatus Status, string Code, string Message, int? LineIndex = null);

/// <summary>
/// Whether engine V1 covers an operation. Anything but <see cref="TaxCoverageStatus.Supported"/>
/// stops the system calculation — it is never computed as zero. <see cref="Assumptions"/> say what
/// a supported calculation takes as given.
/// </summary>
public sealed record TaxCoverageDecision(
    TaxCoverageStatus Status, string PolicyVersion, IReadOnlyList<TaxCoverageFinding> Findings, IReadOnlyList<string> Assumptions)
{
    public bool Supported => Status == TaxCoverageStatus.Supported;
}

/// <summary>
/// The supported-operation matrix of engine V1 (FAIL CLOSED). V1 covers only:
/// <list type="bullet">
/// <item>fact dates in 2026 (the test rates of arts. 343 and 346); 2027+ awaits regulation;</item>
/// <item>a single, immediate supply — operation date = supply date = document date;</item>
/// <item>an establishment in the regular regime or opting for the Simples Nacional; MEI/SIMEI and
/// "Simples above the sub-limit" are not assumed to be the same thing and need professional review;</item>
/// <item>items without a differentiated or specific regime, with the classification the item kind
/// needs (goods: NCM + cClassTrib; services: NBS + cClassTrib). V1 does not interpret cClassTrib.</item>
/// </list>
/// Everything else is refused with its reason. This is a statement of what the software models — not a
/// tax opinion — and its list is pending professional review.
/// </summary>
public static class TaxCoveragePolicyV1
{
    public const string Version = "orken-coverage/1";
    public const string NotSupportedMessage = "Operação não suportada pela versão atual da engine.";

    private static readonly DateOnly First = new(2026, 1, 1);
    private static readonly DateOnly Last = new(2026, 12, 31);

    public static TaxCoverageDecision Assess(TaxOperationRequest op, TaxRegime establishmentRegime)
    {
        var f = new List<TaxCoverageFinding>();

        if (op.OperationDate < First)
            f.Add(new(TaxCoverageStatus.Unsupported, "period-before-ibs-cbs", "Data anterior a 2026: a V1 não calcula IBS/CBS antes do período de teste."));
        else if (op.OperationDate > Last)
            f.Add(new(TaxCoverageStatus.PendingRegulation, "period-pending-regulation",
                "A partir de 2027 as alíquotas dependem de regulamentação ainda não refletida no catálogo: a V1 não calcula."));

        Timing(op, f);
        Regimes(op, establishmentRegime, f);
        foreach (var (line, i) in op.Lines.Select((l, i) => (l, i + 1)))
            Line(line, i, f);

        var status = f.Count == 0 ? TaxCoverageStatus.Supported : f.Select(x => x.Status).OrderByDescending(Severity).First();
        IReadOnlyList<string> assumptions = status == TaxCoverageStatus.Supported
            ? [
                "Fornecimento único e imediato: data da operação = data do fornecimento = data do documento.",
                "Itens sem regime diferenciado ou específico, com a classificação informada; o cClassTrib é registrado, não interpretado pela V1.",
            ]
            : [];
        return new TaxCoverageDecision(status, Version, f, assumptions);
    }

    private static int Severity(TaxCoverageStatus s) => s switch
    {
        TaxCoverageStatus.Unsupported => 5,
        TaxCoverageStatus.PendingRegulation => 4,
        TaxCoverageStatus.RequiresProfessionalReview => 3,
        TaxCoverageStatus.RequiresClassification => 2,
        _ => 0,
    };

    private static void Timing(TaxOperationRequest op, List<TaxCoverageFinding> f)
    {
        var t = op.Timing;
        if (t is null)
        {
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "timing-missing",
                "O padrão temporal da operação não foi informado: a V1 não presume fornecimento único."));
            return;
        }
        switch (t.SupplyPattern)
        {
            case TaxSupplyPattern.ContinuedSupply:
                f.Add(new(TaxCoverageStatus.Unsupported, "timing-continued-supply", "Fornecimento continuado ou fracionado não é modelado pela V1."));
                break;
            case TaxSupplyPattern.AdvancePayment:
                f.Add(new(TaxCoverageStatus.Unsupported, "timing-advance-payment", "Pagamento antecipado não é modelado pela V1."));
                break;
            case TaxSupplyPattern.FutureDelivery:
                f.Add(new(TaxCoverageStatus.Unsupported, "timing-future-delivery", "Entrega futura não é modelada pela V1."));
                break;
            case TaxSupplyPattern.PublicProcurement:
                f.Add(new(TaxCoverageStatus.Unsupported, "timing-public-procurement", "Aquisição por órgão público não é modelada pela V1."));
                break;
            case TaxSupplyPattern.Other:
                f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "timing-other", "Padrão temporal fora dos modelados: precisa de revisão profissional."));
                break;
        }
        if (t.SupplyPattern != TaxSupplyPattern.SingleSupply) return;

        if (t.SupplyDate is not null && t.SupplyDate != op.OperationDate)
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "timing-supply-date",
                "A data do fornecimento difere da data da operação: a V1 não decide qual vale."));
        if (t.DocumentDate is not null && t.DocumentDate != (t.SupplyDate ?? op.OperationDate))
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "timing-document-date",
                "A data do documento difere da data do fornecimento: a V1 não decide qual vale."));
        if (t.PaymentOrAdvanceDate is not null && t.PaymentOrAdvanceDate < (t.SupplyDate ?? op.OperationDate))
            f.Add(new(TaxCoverageStatus.Unsupported, "timing-payment-before-supply", "Pagamento antes do fornecimento (antecipação) não é modelado pela V1."));
        if (t.ExecutionFrom is not null || t.ExecutionTo is not null)
            f.Add(new(TaxCoverageStatus.Unsupported, "timing-execution-period",
                "Período de execução informado indica fornecimento que se estende no tempo, não modelado pela V1."));
    }

    private static void Regimes(TaxOperationRequest op, TaxRegime establishment, List<TaxCoverageFinding> f)
    {
        if (establishment is TaxRegime.Mei or TaxRegime.SimplesNacionalExcessoSublimite)
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-establishment",
                "MEI/SIMEI e Simples acima do sublimite não são tratados como o Simples Nacional comum pela V1: precisa de revisão profissional."));
        if (op.Direction != TaxDirection.Inbound) return;
        if (op.CounterpartyRegime is null)
            f.Add(new(TaxCoverageStatus.Unsupported, "regime-counterparty-missing", "Entrada sem o regime do fornecedor: a V1 não presume o regime."));
        else if (op.CounterpartyRegime is TaxRegime.Mei or TaxRegime.SimplesNacionalExcessoSublimite)
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-counterparty",
                "Fornecedor MEI/SIMEI ou Simples acima do sublimite: a V1 não presume o tratamento; precisa de revisão profissional."));
    }

    private static void Line(TaxLineRequest l, int index, List<TaxCoverageFinding> f)
    {
        if (!string.IsNullOrWhiteSpace(l.SpecialTreatment))
            f.Add(new(TaxCoverageStatus.Unsupported, "line-special-treatment",
                "Item em regime diferenciado ou específico: a V1 não calcula reduções nem bases próprias.", index));

        var missing = new List<string>();
        if (l.ItemKind == TaxItemKind.Good && string.IsNullOrWhiteSpace(l.Ncm)) missing.Add("NCM");
        if (l.ItemKind == TaxItemKind.Service && string.IsNullOrWhiteSpace(l.Nbs)) missing.Add("NBS");
        if (string.IsNullOrWhiteSpace(l.CClassTrib)) missing.Add("cClassTrib");
        if (missing.Count > 0)
            f.Add(new(TaxCoverageStatus.RequiresClassification, "line-classification",
                $"Classificação insuficiente para o recorte: falta {string.Join(" e ", missing)}. A V1 não calcula presumindo o regime geral.", index));
    }
}
