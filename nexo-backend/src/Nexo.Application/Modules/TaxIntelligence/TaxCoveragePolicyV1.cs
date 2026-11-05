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

/// <summary>One cClassTrib code as the catalogue holds it for a date.</summary>
public sealed record TaxClassificationEntry(string Code, bool Active, bool ProfessionallyApproved);

/// <summary>
/// What the matrix needs to know about the establishment and the catalogue: regime, Simples situation, the option
/// for the regular IBS/CBS regime, the cClassTrib codes of the operation as the catalogue holds them on its date, and
/// whether codes must be professionally approved (the definitive mode). A null catalogue skips the code check —
/// used only by tests of the other conditions.
/// </summary>
public sealed record TaxCoverageContext(
    TaxRegime Regime, TaxSimplesSituation SimplesSituation, bool? RegularRegimeOption,
    IReadOnlyDictionary<string, TaxClassificationEntry>? Catalogue, bool RequireApprovedCodes = false);

/// <summary>
/// The supported-operation matrix of engine V1 (FAIL CLOSED). V1 covers only:
/// <list type="bullet">
/// <item>fact dates in 2026 (the test rates of arts. 343 and 346); 2027+ awaits regulation;</item>
/// <item>a single, immediate supply — operation date = supply date = document date;</item>
/// <item>an establishment in the regular regime, or in the Simples Nacional within the sub-limit having stated that it
/// did not opt for the regular IBS/CBS regime; MEI, SIMEI, "above the sub-limit", the regular-regime option and an
/// unknown situation are never assumed and need professional review;</item>
/// <item>items without a differentiated or specific regime, with the classification the item kind
/// needs (goods: NCM + cClassTrib; services: NBS + cClassTrib), the cClassTrib active in the catalogue on the date
/// (and professionally approved in the definitive mode). V1 does not interpret cClassTrib.</item>
/// </list>
/// Everything else is refused with its reason. This is a statement of what the software models — not a
/// tax opinion — and its list is pending professional review.
/// </summary>
public static class TaxCoveragePolicyV1
{
    public const string Version = "orken-coverage/2";
    public const string NotSupportedMessage = "Operação não suportada pela versão atual da engine.";

    private static readonly DateOnly First = new(2026, 1, 1);
    private static readonly DateOnly Last = new(2026, 12, 31);

    public static TaxCoverageDecision Assess(TaxOperationRequest op, TaxCoverageContext context)
    {
        var f = new List<TaxCoverageFinding>();

        if (op.OperationDate < First)
            f.Add(new(TaxCoverageStatus.Unsupported, "period-before-ibs-cbs", "Data anterior a 2026: a V1 não calcula IBS/CBS antes do período de teste."));
        else if (op.OperationDate > Last)
            f.Add(new(TaxCoverageStatus.PendingRegulation, "period-pending-regulation",
                "A partir de 2027 as alíquotas dependem de regulamentação ainda não refletida no catálogo: a V1 não calcula."));

        Timing(op, f);
        Regimes(op, context, f);
        foreach (var (line, i) in op.Lines.Select((l, i) => (l, i + 1)))
            Line(line, i, context, f);

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

    private static void Regimes(TaxOperationRequest op, TaxCoverageContext c, List<TaxCoverageFinding> f)
    {
        Establishment(c, f);
        if (op.Direction != TaxDirection.Inbound) return;
        if (op.CounterpartyRegime is null)
        {
            f.Add(new(TaxCoverageStatus.Unsupported, "regime-counterparty-missing", "Entrada sem o regime do fornecedor: a V1 não presume o regime."));
            return;
        }
        var situation = op.CounterpartySimplesSituation ?? TaxProfileVersion.SituationOf(op.CounterpartyRegime.Value);
        switch (situation)
        {
            case TaxSimplesSituation.Mei or TaxSimplesSituation.Simei or TaxSimplesSituation.AboveSublimit:
                f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-counterparty",
                    "Fornecedor MEI/SIMEI ou Simples acima do sublimite: a V1 não presume o tratamento; precisa de revisão profissional."));
                break;
            case TaxSimplesSituation.Unknown:
                f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-counterparty-unknown",
                    "A situação do fornecedor no Simples Nacional não foi informada: a V1 não presume o tratamento."));
                break;
            case TaxSimplesSituation.WithinSublimit when op.CounterpartyRegularRegimeOption is null:
                f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-counterparty-option-unknown",
                    "Fornecedor do Simples sem a informação de opção pelo regime regular do IBS/CBS: a V1 não presume nenhuma das duas respostas."));
                break;
            case TaxSimplesSituation.WithinSublimit when op.CounterpartyRegularRegimeOption == true:
                f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-counterparty-regular-option",
                    "Fornecedor do Simples que optou pelo regime regular do IBS/CBS: tratamento não modelado pela V1; precisa de revisão profissional."));
                break;
        }
    }

    /// <summary>Only the regular regime, or the common Simples that stated it did not opt for the regular regime, is calculated.</summary>
    private static void Establishment(TaxCoverageContext c, List<TaxCoverageFinding> f)
    {
        switch (c.SimplesSituation)
        {
            case TaxSimplesSituation.NotOptant:
                return;
            case TaxSimplesSituation.Mei or TaxSimplesSituation.Simei or TaxSimplesSituation.AboveSublimit:
                f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-establishment",
                    "MEI/SIMEI e Simples acima do sublimite não são tratados como o Simples Nacional comum pela V1: precisa de revisão profissional."));
                return;
            case TaxSimplesSituation.Unknown:
                f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-establishment-unknown",
                    "A situação do estabelecimento no Simples Nacional não foi informada no perfil: a V1 não presume o regime."));
                return;
        }
        if (c.RegularRegimeOption is null)
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-establishment-option-unknown",
                "O perfil não informa se o estabelecimento optou pelo regime regular do IBS/CBS: a V1 não presume nenhuma das duas respostas."));
        else if (c.RegularRegimeOption == true)
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "regime-establishment-regular-option",
                "Optante do Simples que escolheu o regime regular do IBS/CBS: tratamento não modelado pela V1; precisa de revisão profissional."));
    }

    private static void Line(TaxLineRequest l, int index, TaxCoverageContext c, List<TaxCoverageFinding> f)
    {
        if (!string.IsNullOrWhiteSpace(l.SpecialTreatment))
            f.Add(new(TaxCoverageStatus.Unsupported, "line-special-treatment",
                "Item em regime diferenciado ou específico: a V1 não calcula reduções nem bases próprias.", index));

        var missing = new List<string>();
        if (l.ItemKind == TaxItemKind.Good && string.IsNullOrWhiteSpace(l.Ncm)) missing.Add("NCM");
        if (l.ItemKind == TaxItemKind.Service && string.IsNullOrWhiteSpace(l.Nbs)) missing.Add("NBS");
        if (string.IsNullOrWhiteSpace(l.CClassTrib)) missing.Add("cClassTrib");
        if (missing.Count > 0)
        {
            f.Add(new(TaxCoverageStatus.RequiresClassification, "line-classification",
                $"Classificação insuficiente para o recorte: falta {string.Join(" e ", missing)}. A V1 não calcula presumindo o regime geral.", index));
            return;
        }
        if (c.Catalogue is null) return;
        var code = l.CClassTrib!.Trim().ToUpperInvariant();
        if (!c.Catalogue.TryGetValue(code, out var entry) || !entry.Active)
            f.Add(new(TaxCoverageStatus.RequiresClassification, "line-classification-not-cataloged",
                $"O cClassTrib {code} não está ativo no catálogo de classificação na data da operação: a V1 não calcula.", index));
        else if (c.RequireApprovedCodes && !entry.ProfessionallyApproved)
            f.Add(new(TaxCoverageStatus.RequiresProfessionalReview, "line-classification-review",
                $"O cClassTrib {code} ainda não tem revisão profissional aprovada: o modo definitivo não calcula com ele.", index));
    }
}
