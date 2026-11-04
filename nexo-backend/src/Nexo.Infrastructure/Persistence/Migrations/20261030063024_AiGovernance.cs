using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_usage_reservations",
                schema: "nexo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MonthStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReservedMicros = table.Column<long>(type: "bigint", nullable: false),
                    ReservedTokens = table.Column<long>(type: "bigint", nullable: false),
                    ActualMicros = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usage_reservations", x => x.Id);
                });

            // Data: one active version per type before the unique index (keeps the newest), and the stored hashes become the
            // real SHA-256 of the content (the seed had placeholders like "seed0001"), as the runtime computes them.
            migrationBuilder.Sql(@"UPDATE nexo.stored_prompt_versions p SET ""IsActive"" = false
                WHERE p.""IsActive"" AND EXISTS (SELECT 1 FROM nexo.stored_prompt_versions q
                    WHERE q.""PromptType"" = p.""PromptType"" AND q.""IsActive"" AND (q.""CreatedAt"", q.""Id"") > (p.""CreatedAt"", p.""Id""));");
            migrationBuilder.Sql(@"UPDATE nexo.stored_prompt_versions
                SET ""Hash"" = left(encode(sha256(convert_to(""Content"", 'UTF8')), 'hex'), 16);");

            migrationBuilder.CreateIndex(
                name: "ux_stored_prompt_versions_one_active",
                schema: "nexo",
                table: "stored_prompt_versions",
                column: "PromptType",
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_provider_month",
                schema: "nexo",
                table: "ai_usage_reservations",
                columns: new[] { "Provider", "MonthStart", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_tenant_month",
                schema: "nexo",
                table: "ai_usage_reservations",
                columns: new[] { "TenantId", "MonthStart", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage_reservations",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "ux_stored_prompt_versions_one_active",
                schema: "nexo",
                table: "stored_prompt_versions");
        }
    }
}
