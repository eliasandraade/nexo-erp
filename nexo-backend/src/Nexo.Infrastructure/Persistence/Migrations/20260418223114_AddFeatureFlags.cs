using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_flags",
                schema: "nexo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DefaultEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "geral"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_flags", x => x.Id);
                    table.UniqueConstraint("AK_feature_flags_Key", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "tenant_feature_overrides",
                schema: "nexo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlagKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_feature_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_feature_overrides_feature_flags_FlagKey",
                        column: x => x.FlagKey,
                        principalSchema: "nexo",
                        principalTable: "feature_flags",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tenant_feature_overrides_tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_feature_flags_Category",
                schema: "nexo",
                table: "feature_flags",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_feature_flags_Key",
                schema: "nexo",
                table: "feature_flags",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_feature_overrides_FlagKey",
                schema: "nexo",
                table: "tenant_feature_overrides",
                column: "FlagKey");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_feature_overrides_TenantId_FlagKey",
                schema: "nexo",
                table: "tenant_feature_overrides",
                columns: new[] { "TenantId", "FlagKey" },
                unique: true);

            // ── Seed initial feature flags ───────────────────────────────────
            var now = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                schema: "nexo",
                table: "feature_flags",
                columns: new[] { "Id", "Key", "Name", "Description", "DefaultEnabled", "Category", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { Guid.Parse("11111111-0001-0000-0000-000000000000"), "pdv-desconto-gerente",
                      "PDV — Desconto exige gerente", "Quando ativo, descontos acima do limite configurado exigem autorização de um usuário Gerente ou Diretoria.",
                      false, "pdv", now, now },

                    { Guid.Parse("11111111-0002-0000-0000-000000000000"), "pdv-venda-fiado",
                      "PDV — Permitir venda fiado", "Quando ativo, o operador pode registrar vendas a prazo vinculadas a um cliente cadastrado.",
                      false, "pdv", now, now },

                    { Guid.Parse("11111111-0003-0000-0000-000000000000"), "estoque-ajuste-negativo",
                      "Estoque — Permitir ajuste negativo", "Quando ativo, permite ajustes que resultem em quantidade negativa no estoque.",
                      false, "estoque", now, now },

                    { Guid.Parse("11111111-0004-0000-0000-000000000000"), "restaurante-taxa-servico-automatica",
                      "Restaurante — Taxa de serviço automática", "Quando ativo, a taxa de serviço é aplicada automaticamente ao fechar a comanda.",
                      true, "restaurante", now, now },

                    { Guid.Parse("11111111-0005-0000-0000-000000000000"), "restaurante-couvert-automatico",
                      "Restaurante — Couvert automático", "Quando ativo, o couvert é cobrado automaticamente ao abrir uma mesa.",
                      false, "restaurante", now, now },

                    { Guid.Parse("11111111-0006-0000-0000-000000000000"), "relatorios-exportar-csv",
                      "Relatórios — Exportar CSV", "Quando ativo, os usuários podem exportar tabelas de relatórios em formato CSV.",
                      true, "geral", now, now },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_feature_overrides",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "feature_flags",
                schema: "nexo");
        }
    }
}
