using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// The Build AI prompt types (versioned in the SuperAdmin, <c>stored_prompt_versions</c>) and their built-in defaults, used
/// while no version of the type is active. Only the task is editable: the output contract (JSON schema) and the data-handling
/// rules are fixed in code and always appended. Defaults are constants, so their hash is stable across deployments.
/// </summary>
public static class BuildAiPrompts
{
    public const string DailyLogCopilot      = "build-daily-log-copilot";
    public const string ProgressInterpreter  = "build-progress-interpreter";
    public const string ModelMapper          = "build-model-mapper";
    public const string ClientUpdate         = "build-client-update";
    public const string DelayRisk            = "build-delay-risk";
    public const string CostRisk             = "build-cost-risk";
    public const string DocumentIntelligence = "build-document-intelligence";
    public const string WeeklyDigest         = "build-weekly-digest";

    public static readonly IReadOnlyList<string> Types =
        [DailyLogCopilot, ProgressInterpreter, ModelMapper, ClientUpdate, DelayRisk, CostRisk, DocumentIntelligence, WeeklyDigest];

    private const string Common =
        " Escreva em português do Brasil. Use somente os dados fornecidos e cite os ids de evidência que recebeu. "
        + "Nunca invente ids, números, valores ou datas; na dúvida, deixe vazio e reduza a confiança (0 a 1). Responda só com o JSON.";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DailyLogCopilot] =
            "Você organiza o diário de obra de uma construtora brasileira. A partir do relato livre do dia, monte um rascunho "
            + "estruturado: atividades (cada uma ligada a uma das etapas listadas, pelo id exato), equipe por função e quantidade, "
            + "equipamentos, ocorrências/problemas, observação de clima e um resumo neutro para o cliente (sem custos internos, "
            + "sem culpar pessoas ou fornecedores, sem prometer datas)." + Common,
        [ProgressInterpreter] =
            "Você interpreta o avanço físico de uma obra a partir dos registros recentes do diário (e fotos, quando enviadas). "
            + "Para cada etapa com evidência, sugira um percentual acumulado (nunca abaixo do atual, nunca acima de 100), "
            + "explique o motivo e liste as evidências usadas. Não sugira etapas sem evidência." + Common,
        [ModelMapper] =
            "Você liga partes de um modelo BIM (categorias IFC e zonas, com contagens) às etapas da obra. Proponha vínculos "
            + "de categoria ou zona para cada etapa que claramente corresponda, com justificativa curta. Use só os ids de "
            + "etapa e de zona fornecidos." + Common,
        [ClientUpdate] =
            "Você escreve uma atualização para o cliente final de uma obra, a partir apenas de fatos já publicados ou "
            + "aprovados. Tom amigável, claro e factual; sem custos internos (a não ser que o resumo financeiro seja visível), "
            + "sem prometer datas além das informadas como visíveis, sem jargão. Devolva título, texto e destaques." + Common,
        [DelayRisk] =
            "Você explica, para a construtora, os riscos de prazo já calculados pelo Orken. Os números vêm prontos: não "
            + "calcule nem acrescente números. Resuma a situação, explique cada item pelo seu código e recomende ações "
            + "práticas." + Common,
        [CostRisk] =
            "Você explica, para a construtora, os riscos de custo já calculados pelo Orken (desempenho de custo, estimativa "
            + "no término, aditivos). Os números vêm prontos: não calcule nem acrescente números. Resuma, explique cada item "
            + "pelo seu código e recomende ações práticas." + Common,
        [DocumentIntelligence] =
            "Você lê um documento de obra (contrato, projeto, ART/RRT, alvará, nota fiscal, laudo, manual, garantia) e "
            + "extrai: tipo, emissor, CNPJ do emissor, número, datas de emissão/vencimento/validade, valor total, itens e um "
            + "resumo curto. Copie exatamente o que está escrito; não deduza." + Common,
        [WeeklyDigest] =
            "Você escreve o resumo semanal da obra para o cliente final, a partir apenas dos fatos publicados ou aprovados "
            + "dos últimos 7 dias. Tom amigável e factual; sem custos internos (a não ser que o resumo financeiro seja visível), "
            + "sem prometer datas. Devolva título, texto e destaques." + Common,
    };

    public static string For(BuildAiCapability capability) => capability switch
    {
        BuildAiCapability.DailyLogCopilot => DailyLogCopilot,
        BuildAiCapability.ProgressInterpreter => ProgressInterpreter,
        BuildAiCapability.ModelMapper => ModelMapper,
        BuildAiCapability.ClientUpdate => ClientUpdate,
        BuildAiCapability.DelayRisk => DelayRisk,
        BuildAiCapability.CostRisk => CostRisk,
        BuildAiCapability.DocumentIntelligence => DocumentIntelligence,
        _ => WeeklyDigest,
    };
}
