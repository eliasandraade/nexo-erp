using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiProviderRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                schema: "nexo",
                table: "interpreter_telemetry",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApiKeyUpdatedAt",
                schema: "nexo",
                table: "ai_providers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CostPerMillionInputMicros",
                schema: "nexo",
                table: "ai_providers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CostPerMillionOutputMicros",
                schema: "nexo",
                table: "ai_providers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTestAt",
                schema: "nexo",
                table: "ai_providers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestMessage",
                schema: "nexo",
                table: "ai_providers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestStatus",
                schema: "nexo",
                table: "ai_providers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // Data: at most one default before the unique index (keeps the one with the lowest priority), and the model id
            // seeded before this version ("claude-haiku-20240307") never existed at Anthropic: move it to the current Haiku
            // with its list price per million tokens. Nothing about API keys is touched here.
            migrationBuilder.Sql(@"UPDATE nexo.ai_providers SET ""IsDefault"" = false
                WHERE ""IsDefault"" AND ""Id"" <> (SELECT ""Id"" FROM nexo.ai_providers WHERE ""IsDefault"" ORDER BY ""Priority"", ""Id"" LIMIT 1);");
            migrationBuilder.Sql(@"UPDATE nexo.ai_providers
                SET ""ModelId"" = 'claude-haiku-5-5', ""CostPerMillionInputMicros"" = 100000, ""CostPerMillionOutputMicros"" = 500000
                WHERE ""Provider"" = 'Claude' AND ""ModelId"" = 'claude-haiku-20240307';");

            migrationBuilder.CreateIndex(
                name: "ux_ai_providers_single_default",
                schema: "nexo",
                table: "ai_providers",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ai_providers_single_default",
                schema: "nexo",
                table: "ai_providers");

            migrationBuilder.DropColumn(
                name: "ModelId",
                schema: "nexo",
                table: "interpreter_telemetry");

            migrationBuilder.DropColumn(
                name: "ApiKeyUpdatedAt",
                schema: "nexo",
                table: "ai_providers");

            migrationBuilder.DropColumn(
                name: "CostPerMillionInputMicros",
                schema: "nexo",
                table: "ai_providers");

            migrationBuilder.DropColumn(
                name: "CostPerMillionOutputMicros",
                schema: "nexo",
                table: "ai_providers");

            migrationBuilder.DropColumn(
                name: "LastTestAt",
                schema: "nexo",
                table: "ai_providers");

            migrationBuilder.DropColumn(
                name: "LastTestMessage",
                schema: "nexo",
                table: "ai_providers");

            migrationBuilder.DropColumn(
                name: "LastTestStatus",
                schema: "nexo",
                table: "ai_providers");
        }
    }
}
