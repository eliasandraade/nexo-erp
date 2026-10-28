using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    // Internal pilot approval of the 2026 vertical slice, as literal rows — SELF-CONTAINED like the
    // catalogue seeds: no engine, catalogue, DTO or domain type is referenced.
    //
    // This is an INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT, decided by the product owner
    // of Andrade Systems on 27/10/2026 (docs/tax-intelligence/homologation/internal-pilot-approval-2026.md).
    // It is NOT a professional tax opinion: professional_review, professional_reviewed_by and
    // professional_reference are not touched and stay Pending/empty. Only the nine versions active in
    // 2026 are approved, for 2026 only; the superseded waiver v1 and the 2027+ versions (PendingRegulation,
    // Unverified) are not approved and stay blocked. The caveats travel with every calculation and dossier.
    public partial class TaxControlledPilotApproval
    {
        private const string PilotPolicy = "orken-pilot-approval/1";
        private const string PilotApprovedBy = "Product Owner — Andrade Systems (decisão interna de governança)";
        private const string PilotDecisionRef = "docs/tax-intelligence/homologation/internal-pilot-approval-2026.md";
        private const string PilotScope =
            "Recorte suportado do ORKEN Tax Intelligence v1 (IBS/CBS 2026, matriz orken-coverage/1, engine orken-tax-engine/1.0.0) em piloto controlado. "
            + "Aprovação interna de produto e governança; não constitui parecer jurídico, contábil ou tributário profissional. Operações fora do recorte continuam bloqueadas.";
        private static readonly DateTime PilotApprovedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateOnly PilotFrom = new DateOnly(2026, 1, 1);
        private static readonly DateOnly PilotTo = new DateOnly(2026, 12, 31);

        // version id → caveats accepted with the approval (by rule).
        private static readonly (Guid Id, string Caveats)[] PilotApprovals =
        {
            (new Guid("7a9c3e00-0000-4000-8000-000000000001"), // IBSCBS.BASE.SURCHARGES v1
                "Acréscimos cobrados com o preço integram a base; o que é acréscimo depende dos fatos informados. Cálculo nominal não é recolhimento nem efeito financeiro."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000002"), // IBSCBS.BASE.CONDITIONAL-DISCOUNT v1
                "Desconto condicional permanece na base. Se um desconto é condicional ou incondicional depende da operação e da documentação, não do rótulo informado."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000003"), // IBSCBS.BASE.UNCONDITIONAL-DISCOUNT v1
                "Só sai da base o desconto que reduz efetivamente o preço, consta do documento fiscal e não depende de evento posterior. Desconto escrito na NF não é automaticamente incondicional; exige evidência comercial e confirmação na execução. O documento fiscal sozinho não prova toda a realidade comercial."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000004"), // IBSCBS.BASE.TRANSITION-TAXES v1
                "ICMS, ISS, PIS e Cofins informados saem da base (2026–2032); esta aprovação cobre apenas 2026. Os valores excluídos dependem do que for informado: sem eles, a base pode ficar maior que a real."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000005"), // CBS.RATE.2026-TEST v1
                "Alíquota nominal de teste de 0,9% em 2026. Cálculo nominal é separado de recolhimento e de efeito financeiro (compensação ou dispensa, art. 348). Não se aplica ao Simples Nacional nem a itens em regime diferenciado ou específico. Classificação fiscal não substitui a análise da operação."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000006"), // IBS.RATE.2026-TEST-STATE v1
                "Alíquota nominal de teste de 0,1% (estadual) em 2026; o IBS municipal não é calculado em 2026 nesta versão. Cálculo nominal é separado de recolhimento e de efeito financeiro. Não se aplica ao Simples Nacional nem a itens em regime diferenciado ou específico."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000007"), // IBSCBS.NOTE.2026-SIMPLES v1
                "Nota informativa: o IBS/CBS dentro do Simples Nacional não é calculado. MEI/SIMEI e Simples acima do sublimite continuam dependendo de revisão profissional e não são calculados."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000008"), // IBSCBS.NOTE.2026-COMPENSATION v1
                "Nota informativa sobre a compensação do art. 348, I e II: não calcula o valor compensado nem o efeito financeiro. Impacto financeiro não determinado."),
            (new Guid("7a9c3e00-0000-4000-8000-000000000109"), // IBSCBS.NOTE.2026-WAIVER v2
                "Só dispara com avaliação de obrigações acessórias Verified no período e no escopo indicados. Compliance interno não equivale a regularidade jurídica universal; RemediationVerified comprova somente o escopo verificado e não produz dispensa. Quais documentos comprovam cada obrigação depende de revisão profissional."),
        };

        private static void SeedPilotApproval(MigrationBuilder migrationBuilder)
        {
            foreach (var (id, caveats) in PilotApprovals)
                migrationBuilder.UpdateData(
                    schema: "nexo",
                    table: "tax_rule_versions",
                    keyColumns: new[] { "id" },
                    keyColumnTypes: new[] { "uuid" },
                    keyValues: new object[] { id },
                    columns: new[] { "internal_approval", "internal_approval_policy", "internal_approved_by", "internal_approved_at",
                        "internal_approval_scope", "internal_approval_valid_from", "internal_approval_valid_to", "internal_approval_caveats",
                        "internal_approval_decision_ref" },
                    columnTypes: new[] { "character varying(40)", "character varying(60)", "character varying(200)", "timestamptz",
                        "character varying(2000)", "date", "date", "character varying(4000)", "character varying(300)" },
                    values: new object[] { "ApprovedForControlledPilot", PilotPolicy, PilotApprovedBy, PilotApprovedAt,
                        PilotScope, PilotFrom, PilotTo, caveats, PilotDecisionRef });
        }
    }
}
