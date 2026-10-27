using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    // Catalogue V1.1, as literal rows — SELF-CONTAINED like the Foundation seed: no engine, catalogue,
    // canonicaliser, DTO or domain type is referenced. IBSCBS.NOTE.2026-WAIVER v2 changes only the text of
    // the note (v1 said "Perfil declara…", but the rule fires only on a verified assessment); conditions,
    // validity and priority are those of v1. v1 is not edited: only its status moves to Superseded, so the
    // calculations made with it reproduce. v2 starts with its professional review Pending.
    public partial class TaxIntelligenceReviewDelta
    {
        private const string CatalogV11Origin = "seed:tax-catalog-v1.1";
        private static readonly DateTime CatalogV11SeededAt = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
        private static readonly Guid WaiverRuleId = new Guid("7a9c2e00-0000-4000-8000-000000000009");
        private static readonly Guid WaiverV1Id = new Guid("7a9c3e00-0000-4000-8000-000000000009");
        private static readonly Guid WaiverV2Id = new Guid("7a9c3e00-0000-4000-8000-000000000109");
        private static readonly Guid Art348SourceId = new Guid("7a9c1e00-0000-4000-8000-000000000009");

        private static void SeedCatalogV11(MigrationBuilder migrationBuilder)
        {
            // Conditions and result in the canonical JSON of engine V1; content_hash is its SHA-256 fingerprint.
            migrationBuilder.InsertData(
                schema: "nexo",
                table: "tax_rule_versions",
                columns: new[] { "id", "rule_id", "version", "status", "valid_from", "valid_to", "priority", "conditions", "result", "content_hash", "origin", "created_by", "notes", "activated_at", "status_changed_at", "professional_review", "created_at", "updated_at" },
                columnTypes: new[] { "uuid", "uuid", "integer", "character varying(30)", "date", "date", "integer", "json", "json", "character varying(64)", "character varying(60)", "character varying(200)", "character varying(2000)", "timestamptz", "timestamptz", "character varying(20)", "timestamptz", "timestamptz" },
                values: new object[,]
                {
                    { WaiverV2Id, WaiverRuleId, 2, "Active", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 30,
                      "[{\"kind\":\"direction-is\",\"direction\":\"Outbound\"},{\"kind\":\"supplier-regime-not-in\",\"regimes\":[\"SimplesNacional\",\"SimplesNacionalExcessoSublimite\",\"Mei\"]},{\"kind\":\"profile-flag-is\",\"flag\":\"accessoryObligationsCompliant\",\"value\":true}]",
                      "{\"kind\":\"note\",\"code\":\"IBSCBS-2026-WAIVER\",\"text\":\"Obriga\\u00E7\\u00F5es acess\\u00F3rias verificadas no per\\u00EDodo e no escopo indicados: o art. 348, \\u00A7 1\\u00BA, da LC 214/2025 dispensa o recolhimento do IBS e da CBS de 2026 de quem cumpre as obriga\\u00E7\\u00F5es acess\\u00F3rias; PIS e Cofins continuam devidos integralmente (\\u00A7 2\\u00BA).\"}",
                      "87345221b058670aee375166c8e97025a230c28793ad18639261a40a47e55121", CatalogV11Origin, "Orken (catálogo inicial)",
                      "Nova redação da nota: a v1 dizia \"Perfil declara…\", mas a regra só dispara com cumprimento verificado. Condições, vigência e prioridade iguais às da v1. Revisão profissional pendente.",
                      CatalogV11SeededAt, CatalogV11SeededAt, "Pending", CatalogV11SeededAt, CatalogV11SeededAt },
                });

            migrationBuilder.InsertData(
                schema: "nexo",
                table: "tax_rule_version_sources",
                columns: new[] { "rule_version_id", "legal_source_id" },
                columnTypes: new[] { "uuid", "uuid" },
                values: new object[,] { { WaiverV2Id, Art348SourceId } });

            // v1 is fully covered by v2: its status moves to Superseded; its content never changes.
            migrationBuilder.UpdateData(
                schema: "nexo",
                table: "tax_rule_versions",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "uuid" },
                keyValues: new object[] { WaiverV1Id },
                columns: new[] { "status", "status_changed_at", "updated_at" },
                columnTypes: new[] { "character varying(30)", "timestamptz", "timestamptz" },
                values: new object[] { "Superseded", CatalogV11SeededAt, CatalogV11SeededAt });
        }

        private static void UnseedCatalogV11(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "nexo",
                table: "tax_rule_versions",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "uuid" },
                keyValues: new object[] { WaiverV1Id },
                columns: new[] { "status", "status_changed_at" },
                columnTypes: new[] { "character varying(30)", "timestamptz" },
                values: new object[] { "Active", new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc) });
            migrationBuilder.DeleteData(schema: "nexo", table: "tax_rule_version_sources",
                keyColumns: new[] { "rule_version_id", "legal_source_id" }, keyColumnTypes: new[] { "uuid", "uuid" },
                keyValues: new object[] { WaiverV2Id, Art348SourceId });
            migrationBuilder.DeleteData(schema: "nexo", table: "tax_rule_versions", keyColumn: "id", keyColumnType: "uuid", keyValue: WaiverV2Id);
        }
    }
}
