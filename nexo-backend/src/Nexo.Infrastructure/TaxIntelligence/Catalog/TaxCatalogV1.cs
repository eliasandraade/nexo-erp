using Microsoft.EntityFrameworkCore.Migrations;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.TaxIntelligence.Catalog;

/// <summary>
/// First content of the tax catalogue, inserted by the TaxIntelligenceFoundation migration.
/// FROZEN: never edit this class — a change of law or a correction is a new rule version added by a
/// new migration (or through the platform API). The unit tests pin the content hashes, so an edit
/// here, or in the canonical form, fails the build.
///
/// Every excerpt was copied from the compiled text of LC 214/2025 on the Planalto site (which already
/// carries the changes of LC 227/2026) on 26/10/2026. "Verified" means the excerpt matches the
/// official text — it is not a legal opinion; a tax professional should review the catalogue before
/// it is relied on.
/// </summary>
public static class TaxCatalogV1
{
    public const string Origin = "seed:tax-catalog-v1";
    private const string Url = "https://www.planalto.gov.br/ccivil_03/leis/lcp/Lcp214compilado.htm";
    private const string Lc214 = "Lei Complementar nº 214, de 16 de janeiro de 2025";
    private const string VerifiedBy = "Conferência literal com o texto compilado do Planalto (Orken Tax Intelligence v1) — pendente de revisão por profissional tributário";
    private static readonly DateOnly Published = new(2025, 1, 16);
    private static readonly DateTime SeededAt = new(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);

    public sealed record SourceSeed(Guid Id, string Code, string Reference, string Excerpt);

    public sealed record RuleSeed(
        Guid RuleId, string Key, string Name, string Description,
        Guid VersionId, TaxRuleStatus Status, DateOnly ValidFrom, DateOnly? ValidTo, int Priority,
        IReadOnlyList<TaxCondition> Conditions, TaxRuleResult Result, IReadOnlyList<string> SourceCodes, string? Notes)
    {
        public string ContentHash => TaxCanonical.RuleContentHash(Key, 1, ValidFrom, ValidTo, Priority, Conditions, Result);
    }

    private static Guid S(int n) => Guid.Parse($"7a9c1e00-0000-4000-8000-{n:D12}");
    private static Guid R(int n) => Guid.Parse($"7a9c2e00-0000-4000-8000-{n:D12}");
    private static Guid V(int n) => Guid.Parse($"7a9c3e00-0000-4000-8000-{n:D12}");

    private static readonly IReadOnlyList<TaxRegime> Simples =
        [TaxRegime.SimplesNacional, TaxRegime.SimplesNacionalExcessoSublimite, TaxRegime.Mei];

    public static readonly IReadOnlyList<SourceSeed> Sources =
    [
        new(S(1), TaxLegalCodes.Lc214Art10, "art. 10, caput",
            "Art. 10. Considera-se ocorrido o fato gerador do IBS e da CBS no momento do fornecimento nas operações com bens ou com serviços, ainda que de execução continuada ou fracionada."),
        new(S(2), TaxLegalCodes.Lc214Art12Base, "art. 12, caput e § 1º",
            "Art. 12. A base de cálculo do IBS e da CBS é o valor da operação, salvo disposição em contrário prevista nesta Lei Complementar.\n"
            + "§ 1º O valor da operação compreende o valor integral cobrado pelo fornecedor a qualquer título, inclusive os valores correspondentes a:\n"
            + "I - acréscimos decorrentes de ajuste do valor da operação;\n"
            + "II - juros, multas, acréscimos e encargos;\n"
            + "III - descontos concedidos sob condição;\n"
            + "IV - valor do transporte cobrado como parte do valor da operação, no transporte efetuado pelo próprio fornecedor ou no transporte por sua conta e ordem;\n"
            + "V - tributos e preços públicos, inclusive tarifas, incidentes sobre a operação ou suportados pelo fornecedor, exceto aqueles previstos no § 2º deste artigo; e\n"
            + "VI - demais importâncias cobradas ou recebidas como parte do valor da operação, inclusive seguros e taxas."),
        new(S(3), TaxLegalCodes.Lc214Art12UnconditionalDiscount, "art. 12, § 2º, III, e § 3º",
            "§ 2º Não integram a base de cálculo do IBS e da CBS:\n"
            + "III - os descontos incondicionais;\n"
            + "§ 3º Para efeitos do disposto no inciso III do § 2º deste artigo, considera-se desconto incondicional a parcela redutora do preço da operação que conste do respectivo documento fiscal e não dependa de evento posterior, inclusive se realizado por meio de programa de fidelidade concedido de forma não onerosa pelo próprio fornecedor."),
        new(S(4), TaxLegalCodes.Lc214Art12TransitionTaxes, "art. 12, § 2º, V",
            "§ 2º Não integram a base de cálculo do IBS e da CBS:\n"
            + "V - o montante incidente na operação dos tributos a que se referem o inciso II do caput do art. 155, o inciso III do caput do art. 156 e a alínea “b” do inciso I e o inciso IV do caput do art. 195 da Constituição Federal, e da Contribuição para os Programas de Integração Social e de Formação do Patrimônio do Servidor Público (Contribuição para o PIS/Pasep) a que se refere o art. 239 da Constituição Federal, de 1º de janeiro de 2026 a 31 de dezembro de 2032;"),
        new(S(5), TaxLegalCodes.Lc214Art31SplitPayment, "art. 31, caput",
            "Art. 31. Nas transações de pagamento relativas a operações com bens ou com serviços, os prestadores de serviços de pagamento eletrônico e as instituições operadoras de sistemas de pagamentos deverão segregar e recolher ao Comitê Gestor do IBS e à RFB, no momento da liquidação financeira da transação (split payment), os valores do IBS e da CBS, de acordo com o disposto nesta Subseção."),
        new(S(6), TaxLegalCodes.Lc214Art47Credits, "art. 47, caput e § 1º",
            "Art. 47. O contribuinte sujeito ao regime regular poderá apropriar créditos do IBS e da CBS quando ocorrer a extinção por qualquer das modalidades previstas no art. 27 dos débitos relativos às operações em que seja adquirente, excetuadas exclusivamente aquelas consideradas de uso ou consumo pessoal, nos termos do art. 57 desta Lei Complementar, e as demais hipóteses previstas nesta Lei Complementar.\n"
            + "§ 1º A apropriação dos créditos de que trata o caput deste artigo:\n"
            + "I - será realizada de forma segregada para o IBS e para a CBS, vedadas, em qualquer hipótese, a compensação de créditos de IBS com valores devidos de CBS e a compensação de créditos de CBS com valores devidos de IBS; e\n"
            + "II - está condicionada à comprovação da operação por meio de documento fiscal eletrônico idôneo."),
        new(S(7), TaxLegalCodes.Lc214Art343, "art. 343, caput",
            "Art. 343. Em relação aos fatos geradores ocorridos de 1º de janeiro a 31 de dezembro de 2026, o IBS será cobrado mediante aplicação da alíquota estadual de 0,1% (um décimo por cento)."),
        new(S(8), TaxLegalCodes.Lc214Art346, "art. 346",
            "Art. 346. Em relação aos fatos geradores ocorridos de 1º de janeiro a 31 de dezembro de 2026, a CBS será cobrada mediante aplicação da alíquota de 0,9% (nove décimos por cento)."),
        new(S(9), TaxLegalCodes.Lc214Art348, "art. 348, caput, I a III e §§ 1º e 2º",
            "Art. 348. Em relação aos fatos geradores ocorridos de 1º de janeiro a 31 de dezembro de 2026:\n"
            + "I - o montante recolhido do IBS e da CBS será compensado com o valor devido, no mesmo período de apuração, das contribuições previstas no art. 195, inciso I, alínea “b”, e inciso IV, e da contribuição para o PIS a que se refere o art. 239, ambos da Constituição Federal;\n"
            + "II - caso o contribuinte não possua débitos suficientes para efetuar a compensação de que trata o inciso I, o valor recolhido poderá ser:\n"
            + "a) compensado com qualquer outro tributo federal, nos termos da legislação; ou\n"
            + "b) ressarcido em até 60 (sessenta) dias, mediante requerimento;\n"
            + "III - as alíquotas do IBS e da CBS previstas nos arts. 343 e 346 desta Lei Complementar:\n"
            + "a) serão aplicadas com a respectiva redução no caso das operações sujeitas a alíquota reduzida, no âmbito de regimes diferenciados de tributação;\n"
            + "b) serão aplicadas em relação aos regimes específicos de que trata esta Lei Complementar, observadas as respectivas bases de cálculo, exceto em relação aos combustíveis e biocombustíveis de que tratam os arts. 172 a 180;\n"
            + "c) não serão aplicadas em relação às operações dos contribuintes optantes pelo Simples Nacional.\n"
            + "§ 1º Fica dispensado o recolhimento do IBS e da CBS relativo aos fatos geradores ocorridos no período indicado no caput em relação aos sujeitos passivos que cumprirem as obrigações acessórias previstas na legislação.\n"
            + "§ 2º O sujeito passivo dispensado do recolhimento na forma do § 1º permanece obrigado ao pagamento integral das Contribuições previstas no art. 195, inciso I, alínea “b”, e inciso IV, e da contribuição para o Programa de Integração Social a que se refere o art. 239, ambos da Constituição Federal."),
        new(S(10), TaxLegalCodes.Lc214Art349, "art. 349, caput, I e II",
            "Art. 349. Observadas a forma de cálculo e os limites previstos nesta Seção, resolução do Senado Federal fixará:\n"
            + "I - para os anos de 2027 a 2033, a alíquota de referência da CBS;\n"
            + "II - para os anos de 2029 a 2033:\n"
            + "a) a alíquota de referência do IBS para os Estados;\n"
            + "b) a alíquota de referência do IBS para os Municípios;\n"
            + "c) a alíquota de referência do IBS para o Distrito Federal, que corresponderá à soma das alíquotas de referência previstas nas alíneas “a” e “b” deste inciso;"),
        new(S(11), "LC214-2025-ART344", "art. 344",
            "Art. 344. Em relação aos fatos geradores ocorridos de 1º de janeiro de 2027 a 31 de dezembro de 2028, o IBS será cobrado à alíquota estadual de 0,05% (cinco centésimos por cento) e à alíquota municipal de 0,05% (cinco centésimos por cento).\n"
            + "Parágrafo único. As alíquotas previstas no caput:\n"
            + "I - serão aplicadas com a respectiva redução no caso das operações sujeitas a alíquota reduzida, no âmbito de regimes diferenciados de tributação;\n"
            + "II - serão aplicadas em relação aos regimes específicos de que trata esta Lei Complementar, observadas as respectivas bases de cálculo, exceto em relação aos combustíveis sujeitos ao regime específico de que tratam os arts. 172 a 180 desta Lei Complementar; e\n"
            + "III - em relação aos combustíveis sujeitos ao regime específico de que tratam os arts. 172 a 180 desta Lei Complementar, as alíquotas de que trata o caput deste artigo serão aplicadas sobre o valor da operação no momento da incidência da CBS.\n"
            + "IV - serão consideradas como alíquotas de referência do IBS para fins do disposto no § 2º do art. 189, no § 8º do art. 485, no § 13 do art. 486 e no § 12 do art. 487."),
    ];

    private static readonly DateOnly Y2026 = new(2026, 1, 1);
    private static readonly DateOnly End2026 = new(2026, 12, 31);

    public static readonly IReadOnlyList<RuleSeed> Rules =
    [
        new(R(1), "IBSCBS.BASE.SURCHARGES", "Acréscimos integram a base",
            "Juros, multas, frete cobrado pelo fornecedor e demais valores cobrados com o preço entram na base do IBS e da CBS.",
            V(1), TaxRuleStatus.Active, Y2026, null, 10,
            [], new BaseInclusion(BaseComponent.Surcharges), [TaxLegalCodes.Lc214Art12Base], null),
        new(R(2), "IBSCBS.BASE.CONDITIONAL-DISCOUNT", "Desconto condicional integra a base",
            "Desconto concedido sob condição (pagamento antecipado, meta, devolução…) não é deduzido da base.",
            V(2), TaxRuleStatus.Active, Y2026, null, 20,
            [], new BaseInclusion(BaseComponent.ConditionalDiscount), [TaxLegalCodes.Lc214Art12Base], null),
        new(R(3), "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT", "Desconto incondicional sai da base",
            "Desconto que consta do documento fiscal e não depende de evento posterior é deduzido da base.",
            V(3), TaxRuleStatus.Active, Y2026, null, 30,
            [], new BaseExclusion(BaseComponent.UnconditionalDiscount), [TaxLegalCodes.Lc214Art12UnconditionalDiscount], null),
        new(R(4), "IBSCBS.BASE.TRANSITION-TAXES", "ICMS, ISS, PIS e Cofins fora da base (2026–2032)",
            "O ICMS, o ISS, o PIS/Pasep e a Cofins incidentes na operação não integram a base do IBS e da CBS de 2026 a 2032.",
            V(4), TaxRuleStatus.Active, Y2026, new DateOnly(2032, 12, 31), 40,
            [], new BaseExclusion(BaseComponent.ExcludedTaxes), [TaxLegalCodes.Lc214Art12TransitionTaxes], null),
        new(R(5), "CBS.RATE.2026-TEST", "CBS de 2026 (alíquota de teste)",
            "CBS de 0,9% sobre os fatos geradores de 2026, exceto para optantes do Simples Nacional. Itens em regime diferenciado ou específico não são calculados por esta versão.",
            V(5), TaxRuleStatus.Active, Y2026, End2026, 10,
            [new SupplierRegimeNotIn(Simples), new ItemGeneralRegime()], new RateResult(TaxType.Cbs, 0.9m),
            [TaxLegalCodes.Lc214Art346, TaxLegalCodes.Lc214Art348], null),
        new(R(6), "IBS.RATE.2026-TEST-STATE", "IBS estadual de 2026 (alíquota de teste)",
            "IBS de 0,1% (alíquota estadual) sobre os fatos geradores de 2026, exceto para optantes do Simples Nacional. Itens em regime diferenciado ou específico não são calculados por esta versão.",
            V(6), TaxRuleStatus.Active, Y2026, End2026, 20,
            [new SupplierRegimeNotIn(Simples), new ItemGeneralRegime()], new RateResult(TaxType.IbsState, 0.1m),
            [TaxLegalCodes.Lc214Art343, TaxLegalCodes.Lc214Art348], null),
        new(R(7), "IBSCBS.NOTE.2026-SIMPLES", "Simples Nacional fora das alíquotas de teste",
            "Avisa que as alíquotas de teste de 2026 não se aplicam às operações de optantes do Simples Nacional.",
            V(7), TaxRuleStatus.Active, Y2026, End2026, 10,
            [new SupplierRegimeIn(Simples)],
            new NoteResult(TaxNoteCodes.Simples2026, "As alíquotas de teste de IBS e CBS de 2026 não se aplicam às operações de optantes do Simples Nacional (LC 214/2025, art. 348, III, c)."),
            [TaxLegalCodes.Lc214Art348], null),
        new(R(8), "IBSCBS.NOTE.2026-COMPENSATION", "Compensação do IBS/CBS de 2026",
            "Avisa que o valor recolhido em 2026 é compensado com PIS/Cofins do mesmo período.",
            V(8), TaxRuleStatus.Active, Y2026, End2026, 20,
            [new DirectionIs(TaxDirection.Outbound), new SupplierRegimeNotIn(Simples)],
            new NoteResult(TaxNoteCodes.Compensation2026, "O IBS e a CBS recolhidos em 2026 são compensados com o PIS e a Cofins devidos no mesmo período; sem débitos suficientes, compensados com outro tributo federal ou ressarcidos em até 60 dias (LC 214/2025, art. 348, I e II)."),
            [TaxLegalCodes.Lc214Art348], null),
        new(R(9), "IBSCBS.NOTE.2026-WAIVER", "Dispensa de recolhimento em 2026",
            "Avisa que quem cumpre as obrigações acessórias está dispensado de recolher o IBS e a CBS de 2026.",
            V(9), TaxRuleStatus.Active, Y2026, End2026, 30,
            [new DirectionIs(TaxDirection.Outbound), new SupplierRegimeNotIn(Simples), new ProfileFlagIs(TaxProfileFlags.AccessoryObligationsCompliant, true)],
            new NoteResult(TaxNoteCodes.Waiver2026, "Perfil declara obrigações acessórias em dia: recolhimento do IBS e da CBS de 2026 dispensado; PIS e Cofins continuam devidos integralmente (LC 214/2025, art. 348, §§ 1º e 2º)."),
            [TaxLegalCodes.Lc214Art348], null),
        new(R(10), "CBS.RATE.REFERENCE-2027-2033", "CBS de 2027 a 2033",
            "A alíquota de referência da CBS de 2027 a 2033 depende de resolução do Senado Federal. Enquanto ela não existir, o Orken não calcula a CBS desses anos.",
            V(10), TaxRuleStatus.PendingRegulation, new DateOnly(2027, 1, 1), new DateOnly(2033, 12, 31), 10,
            [], new NoteResult(TaxNoteCodes.ReferenceRatePending, "Alíquota de referência da CBS de 2027 a 2033 a ser fixada por resolução do Senado Federal (LC 214/2025, art. 349, I)."),
            [TaxLegalCodes.Lc214Art349], "Aguarda a resolução do Senado (art. 349) e a redução de 0,1 ponto de 2027–2028 (art. 347)."),
        new(R(11), "IBS.RATE.2027-2028-STATE", "IBS estadual de 2027 e 2028",
            "IBS estadual de 0,05% de 2027 a 2028 (art. 344).",
            V(11), TaxRuleStatus.Unverified, new DateOnly(2027, 1, 1), new DateOnly(2028, 12, 31), 20,
            [new SupplierRegimeNotIn(Simples), new ItemGeneralRegime()], new RateResult(TaxType.IbsState, 0.05m),
            ["LC214-2025-ART344"], "Alíquota literal do art. 344, mas a aplicação aos optantes do Simples Nacional a partir de 2027 não foi verificada: não ativar antes dessa análise."),
        new(R(12), "IBS.RATE.2027-2028-MUNICIPAL", "IBS municipal de 2027 e 2028",
            "IBS municipal de 0,05% de 2027 a 2028 (art. 344).",
            V(12), TaxRuleStatus.Unverified, new DateOnly(2027, 1, 1), new DateOnly(2028, 12, 31), 30,
            [new SupplierRegimeNotIn(Simples), new ItemGeneralRegime()], new RateResult(TaxType.IbsMunicipal, 0.05m),
            ["LC214-2025-ART344"], "Alíquota literal do art. 344, mas a aplicação aos optantes do Simples Nacional a partir de 2027 não foi verificada: não ativar antes dessa análise."),
    ];

    /// <summary>Inserts the catalogue. Called once, from the TaxIntelligenceFoundation migration.</summary>
    public static void Seed(MigrationBuilder mb)
    {
        foreach (var s in Sources)
        {
            mb.InsertData("tax_legal_sources",
                ["id", "code", "kind", "title", "reference", "url", "excerpt", "published_on", "verification", "verified_at", "verified_by", "notes", "created_at", "updated_at"],
                [s.Id, s.Code, LegalSourceKind.ComplementaryLaw.ToString(), Lc214, s.Reference, Url, s.Excerpt, Published,
                 LegalSourceVerification.Verified.ToString(), SeededAt, VerifiedBy, "Texto compilado, com as alterações da LC 227/2026.", SeededAt, SeededAt],
                "nexo");
        }

        var codeToId = Sources.ToDictionary(s => s.Code, s => s.Id);
        foreach (var r in Rules)
        {
            mb.InsertData("tax_rules",
                ["id", "key", "name", "description", "created_at", "updated_at"],
                [r.RuleId, r.Key, r.Name, r.Description, SeededAt, SeededAt],
                "nexo");

            mb.InsertData("tax_rule_versions",
                ["id", "rule_id", "version", "status", "valid_from", "valid_to", "priority", "conditions", "result", "content_hash",
                 "origin", "created_by", "notes", "activated_at", "status_changed_at", "created_at", "updated_at"],
                ["uuid", "uuid", "integer", "character varying(30)", "date", "date", "integer", "json", "json", "character varying(64)",
                 "character varying(60)", "character varying(200)", "character varying(2000)", "timestamptz", "timestamptz", "timestamptz", "timestamptz"],
                [r.VersionId, r.RuleId, 1, r.Status.ToString(), r.ValidFrom, r.ValidTo, r.Priority,
                 TaxCanonical.Serialize(r.Conditions), TaxCanonical.Serialize(r.Result), r.ContentHash,
                 Origin, "Orken (catálogo inicial)", r.Notes, r.Status == TaxRuleStatus.Active ? SeededAt : null, SeededAt, SeededAt, SeededAt],
                "nexo");

            foreach (var code in r.SourceCodes)
                mb.InsertData("tax_rule_version_sources",
                    ["rule_version_id", "legal_source_id"],
                    [r.VersionId, codeToId[code]],
                    "nexo");
        }
    }
}
