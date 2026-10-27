using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    // Initial content of the tax catalogue (V1), as literal rows. SELF-CONTAINED on purpose: this file
    // uses no engine, catalogue, canonicaliser, DTO or domain type, so no later change of code can
    // change what the migration inserts — a database migrated today and one migrated years from now get
    // byte-identical rows. Conditions and results are the canonical JSON of engine V1
    // ("orken-canonical/1"); content_hash is the SHA-256 fingerprint the engine V1 computed for them.
    // Unit tests check that these rows match the typed description of the catalogue and pin the hashes.
    //
    // Text verification ("Verified") means each excerpt was compared with the official compiled text;
    // every rule version starts with its professional review "Pending" — nothing here is approved by a
    // tax professional. FROZEN: never edit; a correction is a new rule version in a new migration.
    public partial class TaxIntelligenceFoundation
    {
        private const string LawTitle = "Lei Complementar nº 214, de 16 de janeiro de 2025";
        private const string SourceUrl = "https://www.planalto.gov.br/ccivil_03/leis/lcp/Lcp214compilado.htm";
        private const string TextVerifiedBy = "Conferência literal com o texto compilado do Planalto (Orken Tax Intelligence v1) — pendente de revisão por profissional tributário";
        private const string SourceNotes = "Texto compilado, com as alterações da LC 227/2026.";
        private const string Origin = "seed:tax-catalog-v1";
        private const string CreatedBy = "Orken (catálogo inicial)";
        private static readonly DateOnly PublishedOn = new DateOnly(2025, 1, 16);
        private static readonly DateTime SeededAt = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);

        private static void SeedCatalogV1(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "nexo",
                table: "tax_legal_sources",
                columns: new[] { "id", "code", "kind", "title", "reference", "url", "excerpt", "published_on", "text_verification", "verified_at", "verified_by", "notes", "created_at", "updated_at" },
                columnTypes: new[] { "uuid", "character varying(60)", "character varying(40)", "character varying(300)", "character varying(200)", "character varying(500)", "text", "date", "character varying(20)", "timestamptz", "character varying(200)", "character varying(1000)", "timestamptz", "timestamptz" },
                values: new object[,]
                {
                    { new Guid("7a9c1e00-0000-4000-8000-000000000001"), "LC214-2025-ART10", "ComplementaryLaw", LawTitle, "art. 10, caput", SourceUrl, "Art. 10. Considera-se ocorrido o fato gerador do IBS e da CBS no momento do fornecimento nas operações com bens ou com serviços, ainda que de execução continuada ou fracionada.", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000002"), "LC214-2025-ART12-CAPUT-P1", "ComplementaryLaw", LawTitle, "art. 12, caput e § 1º", SourceUrl, "Art. 12. A base de cálculo do IBS e da CBS é o valor da operação, salvo disposição em contrário prevista nesta Lei Complementar.\n§ 1º O valor da operação compreende o valor integral cobrado pelo fornecedor a qualquer título, inclusive os valores correspondentes a:\nI - acréscimos decorrentes de ajuste do valor da operação;\nII - juros, multas, acréscimos e encargos;\nIII - descontos concedidos sob condição;\nIV - valor do transporte cobrado como parte do valor da operação, no transporte efetuado pelo próprio fornecedor ou no transporte por sua conta e ordem;\nV - tributos e preços públicos, inclusive tarifas, incidentes sobre a operação ou suportados pelo fornecedor, exceto aqueles previstos no § 2º deste artigo; e\nVI - demais importâncias cobradas ou recebidas como parte do valor da operação, inclusive seguros e taxas.", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000003"), "LC214-2025-ART12-P2-III-P3", "ComplementaryLaw", LawTitle, "art. 12, § 2º, III, e § 3º", SourceUrl, "§ 2º Não integram a base de cálculo do IBS e da CBS:\nIII - os descontos incondicionais;\n§ 3º Para efeitos do disposto no inciso III do § 2º deste artigo, considera-se desconto incondicional a parcela redutora do preço da operação que conste do respectivo documento fiscal e não dependa de evento posterior, inclusive se realizado por meio de programa de fidelidade concedido de forma não onerosa pelo próprio fornecedor.", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000004"), "LC214-2025-ART12-P2-V", "ComplementaryLaw", LawTitle, "art. 12, § 2º, V", SourceUrl, "§ 2º Não integram a base de cálculo do IBS e da CBS:\nV - o montante incidente na operação dos tributos a que se referem o inciso II do caput do art. 155, o inciso III do caput do art. 156 e a alínea “b” do inciso I e o inciso IV do caput do art. 195 da Constituição Federal, e da Contribuição para os Programas de Integração Social e de Formação do Patrimônio do Servidor Público (Contribuição para o PIS/Pasep) a que se refere o art. 239 da Constituição Federal, de 1º de janeiro de 2026 a 31 de dezembro de 2032;", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000005"), "LC214-2025-ART31", "ComplementaryLaw", LawTitle, "art. 31, caput", SourceUrl, "Art. 31. Nas transações de pagamento relativas a operações com bens ou com serviços, os prestadores de serviços de pagamento eletrônico e as instituições operadoras de sistemas de pagamentos deverão segregar e recolher ao Comitê Gestor do IBS e à RFB, no momento da liquidação financeira da transação (split payment), os valores do IBS e da CBS, de acordo com o disposto nesta Subseção.", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000006"), "LC214-2025-ART47", "ComplementaryLaw", LawTitle, "art. 47, caput e § 1º", SourceUrl, "Art. 47. O contribuinte sujeito ao regime regular poderá apropriar créditos do IBS e da CBS quando ocorrer a extinção por qualquer das modalidades previstas no art. 27 dos débitos relativos às operações em que seja adquirente, excetuadas exclusivamente aquelas consideradas de uso ou consumo pessoal, nos termos do art. 57 desta Lei Complementar, e as demais hipóteses previstas nesta Lei Complementar.\n§ 1º A apropriação dos créditos de que trata o caput deste artigo:\nI - será realizada de forma segregada para o IBS e para a CBS, vedadas, em qualquer hipótese, a compensação de créditos de IBS com valores devidos de CBS e a compensação de créditos de CBS com valores devidos de IBS; e\nII - está condicionada à comprovação da operação por meio de documento fiscal eletrônico idôneo.", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000007"), "LC214-2025-ART343", "ComplementaryLaw", LawTitle, "art. 343, caput", SourceUrl, "Art. 343. Em relação aos fatos geradores ocorridos de 1º de janeiro a 31 de dezembro de 2026, o IBS será cobrado mediante aplicação da alíquota estadual de 0,1% (um décimo por cento).", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000008"), "LC214-2025-ART346", "ComplementaryLaw", LawTitle, "art. 346", SourceUrl, "Art. 346. Em relação aos fatos geradores ocorridos de 1º de janeiro a 31 de dezembro de 2026, a CBS será cobrada mediante aplicação da alíquota de 0,9% (nove décimos por cento).", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000009"), "LC214-2025-ART348", "ComplementaryLaw", LawTitle, "art. 348, caput, I a III e §§ 1º e 2º", SourceUrl, "Art. 348. Em relação aos fatos geradores ocorridos de 1º de janeiro a 31 de dezembro de 2026:\nI - o montante recolhido do IBS e da CBS será compensado com o valor devido, no mesmo período de apuração, das contribuições previstas no art. 195, inciso I, alínea “b”, e inciso IV, e da contribuição para o PIS a que se refere o art. 239, ambos da Constituição Federal;\nII - caso o contribuinte não possua débitos suficientes para efetuar a compensação de que trata o inciso I, o valor recolhido poderá ser:\na) compensado com qualquer outro tributo federal, nos termos da legislação; ou\nb) ressarcido em até 60 (sessenta) dias, mediante requerimento;\nIII - as alíquotas do IBS e da CBS previstas nos arts. 343 e 346 desta Lei Complementar:\na) serão aplicadas com a respectiva redução no caso das operações sujeitas a alíquota reduzida, no âmbito de regimes diferenciados de tributação;\nb) serão aplicadas em relação aos regimes específicos de que trata esta Lei Complementar, observadas as respectivas bases de cálculo, exceto em relação aos combustíveis e biocombustíveis de que tratam os arts. 172 a 180;\nc) não serão aplicadas em relação às operações dos contribuintes optantes pelo Simples Nacional.\n§ 1º Fica dispensado o recolhimento do IBS e da CBS relativo aos fatos geradores ocorridos no período indicado no caput em relação aos sujeitos passivos que cumprirem as obrigações acessórias previstas na legislação.\n§ 2º O sujeito passivo dispensado do recolhimento na forma do § 1º permanece obrigado ao pagamento integral das Contribuições previstas no art. 195, inciso I, alínea “b”, e inciso IV, e da contribuição para o Programa de Integração Social a que se refere o art. 239, ambos da Constituição Federal.", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000010"), "LC214-2025-ART349", "ComplementaryLaw", LawTitle, "art. 349, caput, I e II", SourceUrl, "Art. 349. Observadas a forma de cálculo e os limites previstos nesta Seção, resolução do Senado Federal fixará:\nI - para os anos de 2027 a 2033, a alíquota de referência da CBS;\nII - para os anos de 2029 a 2033:\na) a alíquota de referência do IBS para os Estados;\nb) a alíquota de referência do IBS para os Municípios;\nc) a alíquota de referência do IBS para o Distrito Federal, que corresponderá à soma das alíquotas de referência previstas nas alíneas “a” e “b” deste inciso;", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                    { new Guid("7a9c1e00-0000-4000-8000-000000000011"), "LC214-2025-ART344", "ComplementaryLaw", LawTitle, "art. 344", SourceUrl, "Art. 344. Em relação aos fatos geradores ocorridos de 1º de janeiro de 2027 a 31 de dezembro de 2028, o IBS será cobrado à alíquota estadual de 0,05% (cinco centésimos por cento) e à alíquota municipal de 0,05% (cinco centésimos por cento).\nParágrafo único. As alíquotas previstas no caput:\nI - serão aplicadas com a respectiva redução no caso das operações sujeitas a alíquota reduzida, no âmbito de regimes diferenciados de tributação;\nII - serão aplicadas em relação aos regimes específicos de que trata esta Lei Complementar, observadas as respectivas bases de cálculo, exceto em relação aos combustíveis sujeitos ao regime específico de que tratam os arts. 172 a 180 desta Lei Complementar; e\nIII - em relação aos combustíveis sujeitos ao regime específico de que tratam os arts. 172 a 180 desta Lei Complementar, as alíquotas de que trata o caput deste artigo serão aplicadas sobre o valor da operação no momento da incidência da CBS.\nIV - serão consideradas como alíquotas de referência do IBS para fins do disposto no § 2º do art. 189, no § 8º do art. 485, no § 13 do art. 486 e no § 12 do art. 487.", PublishedOn, "Verified", SeededAt, TextVerifiedBy, SourceNotes, SeededAt, SeededAt },
                });

            migrationBuilder.InsertData(
                schema: "nexo",
                table: "tax_rules",
                columns: new[] { "id", "key", "name", "description", "created_at", "updated_at" },
                columnTypes: new[] { "uuid", "character varying(80)", "character varying(200)", "character varying(2000)", "timestamptz", "timestamptz" },
                values: new object[,]
                {
                    { new Guid("7a9c2e00-0000-4000-8000-000000000001"), "IBSCBS.BASE.SURCHARGES", "Acréscimos integram a base", "Juros, multas, frete cobrado pelo fornecedor e demais valores cobrados com o preço entram na base do IBS e da CBS.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000002"), "IBSCBS.BASE.CONDITIONAL-DISCOUNT", "Desconto condicional integra a base", "Desconto concedido sob condição (pagamento antecipado, meta, devolução…) não é deduzido da base.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000003"), "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT", "Desconto incondicional sai da base", "Desconto que consta do documento fiscal e não depende de evento posterior é deduzido da base.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000004"), "IBSCBS.BASE.TRANSITION-TAXES", "ICMS, ISS, PIS e Cofins fora da base (2026–2032)", "O ICMS, o ISS, o PIS/Pasep e a Cofins incidentes na operação não integram a base do IBS e da CBS de 2026 a 2032.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000005"), "CBS.RATE.2026-TEST", "CBS de 2026 (alíquota de teste)", "CBS de 0,9% sobre os fatos geradores de 2026, exceto para optantes do Simples Nacional. Itens em regime diferenciado ou específico não são calculados por esta versão.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000006"), "IBS.RATE.2026-TEST-STATE", "IBS estadual de 2026 (alíquota de teste)", "IBS de 0,1% (alíquota estadual) sobre os fatos geradores de 2026, exceto para optantes do Simples Nacional. Itens em regime diferenciado ou específico não são calculados por esta versão.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000007"), "IBSCBS.NOTE.2026-SIMPLES", "Simples Nacional fora das alíquotas de teste", "Avisa que as alíquotas de teste de 2026 não se aplicam às operações de optantes do Simples Nacional.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000008"), "IBSCBS.NOTE.2026-COMPENSATION", "Compensação do IBS/CBS de 2026", "Avisa que o valor recolhido em 2026 é compensado com PIS/Cofins do mesmo período.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000009"), "IBSCBS.NOTE.2026-WAIVER", "Dispensa de recolhimento em 2026", "Avisa que quem cumpre as obrigações acessórias está dispensado de recolher o IBS e a CBS de 2026.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000010"), "CBS.RATE.REFERENCE-2027-2033", "CBS de 2027 a 2033", "A alíquota de referência da CBS de 2027 a 2033 depende de resolução do Senado Federal. Enquanto ela não existir, o Orken não calcula a CBS desses anos.", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000011"), "IBS.RATE.2027-2028-STATE", "IBS estadual de 2027 e 2028", "IBS estadual de 0,05% de 2027 a 2028 (art. 344).", SeededAt, SeededAt },
                    { new Guid("7a9c2e00-0000-4000-8000-000000000012"), "IBS.RATE.2027-2028-MUNICIPAL", "IBS municipal de 2027 e 2028", "IBS municipal de 0,05% de 2027 a 2028 (art. 344).", SeededAt, SeededAt },
                });

            // Conditions and result are the canonical JSON of engine V1; content_hash is its SHA-256 fingerprint.
            // Every version starts with the professional review Pending: nothing here is professionally approved.
            migrationBuilder.InsertData(
                schema: "nexo",
                table: "tax_rule_versions",
                columns: new[] { "id", "rule_id", "version", "status", "valid_from", "valid_to", "priority", "conditions", "result", "content_hash", "origin", "created_by", "notes", "activated_at", "status_changed_at", "professional_review", "professional_reviewed_by", "professional_reviewed_at", "professional_review_notes", "created_at", "updated_at" },
                columnTypes: new[] { "uuid", "uuid", "integer", "character varying(30)", "date", "date", "integer", "json", "json", "character varying(64)", "character varying(60)", "character varying(200)", "character varying(2000)", "timestamptz", "timestamptz", "character varying(20)", "character varying(200)", "timestamptz", "character varying(4000)", "timestamptz", "timestamptz" },
                values: new object[,]
                {
                    { new Guid("7a9c3e00-0000-4000-8000-000000000001"), new Guid("7a9c2e00-0000-4000-8000-000000000001"), 1, "Active", new DateOnly(2026, 1, 1), null, 10,
                      "[]",
                      "{\"kind\":\"base-inclusion\",\"component\":\"Surcharges\"}",
                      "a876b32b8467f4079ffd8bd892522c89c2415909660b43a326f45c7b157fbf48", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000002"), new Guid("7a9c2e00-0000-4000-8000-000000000002"), 1, "Active", new DateOnly(2026, 1, 1), null, 20,
                      "[]",
                      "{\"kind\":\"base-inclusion\",\"component\":\"ConditionalDiscount\"}",
                      "6ceb5f96f39abf737b5feccb3f516fefc53fc1de45dcd6db7819ebe30a22b47c", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000003"), new Guid("7a9c2e00-0000-4000-8000-000000000003"), 1, "Active", new DateOnly(2026, 1, 1), null, 30,
                      "[]",
                      "{\"kind\":\"base-exclusion\",\"component\":\"UnconditionalDiscount\"}",
                      "fc56d1469478465d8b7f0bd07029f375c2ba3d64596930ba0baf3aa342ab380b", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000004"), new Guid("7a9c2e00-0000-4000-8000-000000000004"), 1, "Active", new DateOnly(2026, 1, 1), new DateOnly(2032, 12, 31), 40,
                      "[]",
                      "{\"kind\":\"base-exclusion\",\"component\":\"ExcludedTaxes\"}",
                      "8fe5f049b57aab0c71b0bf9bc466262a6ea4b23418e1e160e501e0d92b22ff93", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000005"), new Guid("7a9c2e00-0000-4000-8000-000000000005"), 1, "Active", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 10,
                      "[{\"kind\":\"supplier-regime-not-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]},{\"kind\":\"item-general-regime\"}]",
                      "{\"kind\":\"rate\",\"tax\":\"Cbs\",\"ratePercent\":0.9}",
                      "e2de5c9cde13f0cb9e4f1c8820d84e2401e08bd05ea727cb9994677252fa09c4", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000006"), new Guid("7a9c2e00-0000-4000-8000-000000000006"), 1, "Active", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 20,
                      "[{\"kind\":\"supplier-regime-not-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]},{\"kind\":\"item-general-regime\"}]",
                      "{\"kind\":\"rate\",\"tax\":\"IbsState\",\"ratePercent\":0.1}",
                      "acfeedf5b258de43d760e27a6e9f945b0f8ff541678ced46e1309dbd23e7e76a", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000007"), new Guid("7a9c2e00-0000-4000-8000-000000000007"), 1, "Active", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 10,
                      "[{\"kind\":\"supplier-regime-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]}]",
                      "{\"kind\":\"note\",\"code\":\"IBSCBS-2026-SIMPLES\",\"text\":\"As al\\u00EDquotas de teste de IBS e CBS de 2026 n\\u00E3o se aplicam \\u00E0s opera\\u00E7\\u00F5es de optantes do Simples Nacional (LC 214/2025, art. 348, III, c).\"}",
                      "32489294dadefad3bb8bd6f58c1ee2cd272d8c43f3d1d6d95edb36bd7c39a776", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000008"), new Guid("7a9c2e00-0000-4000-8000-000000000008"), 1, "Active", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 20,
                      "[{\"kind\":\"direction-is\",\"direction\":\"Outbound\"},{\"kind\":\"supplier-regime-not-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]}]",
                      "{\"kind\":\"note\",\"code\":\"IBSCBS-2026-COMPENSATION\",\"text\":\"O IBS e a CBS recolhidos em 2026 s\\u00E3o compensados com o PIS e a Cofins devidos no mesmo per\\u00EDodo; sem d\\u00E9bitos suficientes, compensados com outro tributo federal ou ressarcidos em at\\u00E9 60 dias (LC 214/2025, art. 348, I e II).\"}",
                      "d6130ea8c549084ba1e5cd517915b05c603564f96a2363057634a2c01034755b", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000009"), new Guid("7a9c2e00-0000-4000-8000-000000000009"), 1, "Active", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 30,
                      "[{\"kind\":\"direction-is\",\"direction\":\"Outbound\"},{\"kind\":\"supplier-regime-not-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]},{\"kind\":\"profile-flag-is\",\"flag\":\"accessoryObligationsCompliant\",\"value\":true}]",
                      "{\"kind\":\"note\",\"code\":\"IBSCBS-2026-WAIVER\",\"text\":\"Perfil declara obriga\\u00E7\\u00F5es acess\\u00F3rias em dia: recolhimento do IBS e da CBS de 2026 dispensado; PIS e Cofins continuam devidos integralmente (LC 214/2025, art. 348, \\u00A7\\u00A7 1\\u00BA e 2\\u00BA).\"}",
                      "de2ce6c335fa29976a102f8a07f35c28700195947a4ae8ad1565918f6909f294", Origin, CreatedBy, null, SeededAt, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000010"), new Guid("7a9c2e00-0000-4000-8000-000000000010"), 1, "PendingRegulation", new DateOnly(2027, 1, 1), new DateOnly(2033, 12, 31), 10,
                      "[]",
                      "{\"kind\":\"note\",\"code\":\"IBSCBS-REFERENCE-RATE-PENDING\",\"text\":\"Al\\u00EDquota de refer\\u00EAncia da CBS de 2027 a 2033 a ser fixada por resolu\\u00E7\\u00E3o do Senado Federal (LC 214/2025, art. 349, I).\"}",
                      "7fac0f049da8383e33fe4ddddcb2d5faafa3bbc166d08a73a60b71dd88586046", Origin, CreatedBy, "Aguarda a resolução do Senado (art. 349) e a redução de 0,1 ponto de 2027–2028 (art. 347).", null, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000011"), new Guid("7a9c2e00-0000-4000-8000-000000000011"), 1, "Unverified", new DateOnly(2027, 1, 1), new DateOnly(2028, 12, 31), 20,
                      "[{\"kind\":\"supplier-regime-not-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]},{\"kind\":\"item-general-regime\"}]",
                      "{\"kind\":\"rate\",\"tax\":\"IbsState\",\"ratePercent\":0.05}",
                      "64b44309d851698c5adfe36256f6e286617b5e94b9b49b72ff2aee1b120830c7", Origin, CreatedBy, "Alíquota literal do art. 344, mas a aplicação aos optantes do Simples Nacional a partir de 2027 não foi verificada: não ativar antes dessa análise.", null, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                    { new Guid("7a9c3e00-0000-4000-8000-000000000012"), new Guid("7a9c2e00-0000-4000-8000-000000000012"), 1, "Unverified", new DateOnly(2027, 1, 1), new DateOnly(2028, 12, 31), 30,
                      "[{\"kind\":\"supplier-regime-not-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]},{\"kind\":\"item-general-regime\"}]",
                      "{\"kind\":\"rate\",\"tax\":\"IbsMunicipal\",\"ratePercent\":0.05}",
                      "2149130078d47763b238d871912d12dc1238f529647241dafc6270df54b8991c", Origin, CreatedBy, "Alíquota literal do art. 344, mas a aplicação aos optantes do Simples Nacional a partir de 2027 não foi verificada: não ativar antes dessa análise.", null, SeededAt, "Pending", null, null, null, SeededAt, SeededAt },
                });

            migrationBuilder.InsertData(
                schema: "nexo",
                table: "tax_rule_version_sources",
                columns: new[] { "rule_version_id", "legal_source_id" },
                columnTypes: new[] { "uuid", "uuid" },
                values: new object[,]
                {
                    { new Guid("7a9c3e00-0000-4000-8000-000000000001"), new Guid("7a9c1e00-0000-4000-8000-000000000002") }, // IBSCBS.BASE.SURCHARGES → LC214-2025-ART12-CAPUT-P1
                    { new Guid("7a9c3e00-0000-4000-8000-000000000002"), new Guid("7a9c1e00-0000-4000-8000-000000000002") }, // IBSCBS.BASE.CONDITIONAL-DISCOUNT → LC214-2025-ART12-CAPUT-P1
                    { new Guid("7a9c3e00-0000-4000-8000-000000000003"), new Guid("7a9c1e00-0000-4000-8000-000000000003") }, // IBSCBS.BASE.UNCONDITIONAL-DISCOUNT → LC214-2025-ART12-P2-III-P3
                    { new Guid("7a9c3e00-0000-4000-8000-000000000004"), new Guid("7a9c1e00-0000-4000-8000-000000000004") }, // IBSCBS.BASE.TRANSITION-TAXES → LC214-2025-ART12-P2-V
                    { new Guid("7a9c3e00-0000-4000-8000-000000000005"), new Guid("7a9c1e00-0000-4000-8000-000000000008") }, // CBS.RATE.2026-TEST → LC214-2025-ART346
                    { new Guid("7a9c3e00-0000-4000-8000-000000000005"), new Guid("7a9c1e00-0000-4000-8000-000000000009") }, // CBS.RATE.2026-TEST → LC214-2025-ART348
                    { new Guid("7a9c3e00-0000-4000-8000-000000000006"), new Guid("7a9c1e00-0000-4000-8000-000000000007") }, // IBS.RATE.2026-TEST-STATE → LC214-2025-ART343
                    { new Guid("7a9c3e00-0000-4000-8000-000000000006"), new Guid("7a9c1e00-0000-4000-8000-000000000009") }, // IBS.RATE.2026-TEST-STATE → LC214-2025-ART348
                    { new Guid("7a9c3e00-0000-4000-8000-000000000007"), new Guid("7a9c1e00-0000-4000-8000-000000000009") }, // IBSCBS.NOTE.2026-SIMPLES → LC214-2025-ART348
                    { new Guid("7a9c3e00-0000-4000-8000-000000000008"), new Guid("7a9c1e00-0000-4000-8000-000000000009") }, // IBSCBS.NOTE.2026-COMPENSATION → LC214-2025-ART348
                    { new Guid("7a9c3e00-0000-4000-8000-000000000009"), new Guid("7a9c1e00-0000-4000-8000-000000000009") }, // IBSCBS.NOTE.2026-WAIVER → LC214-2025-ART348
                    { new Guid("7a9c3e00-0000-4000-8000-000000000010"), new Guid("7a9c1e00-0000-4000-8000-000000000010") }, // CBS.RATE.REFERENCE-2027-2033 → LC214-2025-ART349
                    { new Guid("7a9c3e00-0000-4000-8000-000000000011"), new Guid("7a9c1e00-0000-4000-8000-000000000011") }, // IBS.RATE.2027-2028-STATE → LC214-2025-ART344
                    { new Guid("7a9c3e00-0000-4000-8000-000000000012"), new Guid("7a9c1e00-0000-4000-8000-000000000011") }, // IBS.RATE.2027-2028-MUNICIPAL → LC214-2025-ART344
                });
        }
    }
}
