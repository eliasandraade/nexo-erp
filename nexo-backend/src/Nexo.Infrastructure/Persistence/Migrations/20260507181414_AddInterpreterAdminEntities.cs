using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInterpreterAdminEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_providers",
                schema: "nexo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    ApiKeyEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApiKeyLastFour = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ModelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MonthlyTokenLimit = table.Column<long>(type: "bigint", nullable: true),
                    CostPerInputTokenMicros = table.Column<long>(type: "bigint", nullable: false),
                    CostPerOutputTokenMicros = table.Column<long>(type: "bigint", nullable: false),
                    FallbackProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "interpreter_telemetry",
                schema: "nexo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PromptType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostMicros = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FallbackUsed = table.Column<bool>(type: "boolean", nullable: false),
                    FallbackFromProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AnalyzerChainJson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RequiresInputCount = table.Column<int>(type: "integer", nullable: false),
                    AmountConfidence = table.Column<double>(type: "double precision", nullable: false),
                    DateConfidence = table.Column<double>(type: "double precision", nullable: false),
                    RawPrompt = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    RawResponse = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interpreter_telemetry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stored_prompt_versions",
                schema: "nexo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_prompt_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_ai_limits",
                schema: "nexo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftLimitCents = table.Column<int>(type: "integer", nullable: true),
                    HardLimitCents = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_ai_limits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_providers_Priority",
                schema: "nexo",
                table: "ai_providers",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_ai_providers_Provider",
                schema: "nexo",
                table: "ai_providers",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_interpreter_telemetry_CreatedAt",
                schema: "nexo",
                table: "interpreter_telemetry",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_interpreter_telemetry_TenantId",
                schema: "nexo",
                table: "interpreter_telemetry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_interpreter_telemetry_TenantId_CreatedAt",
                schema: "nexo",
                table: "interpreter_telemetry",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_prompt_versions_PromptType_IsActive",
                schema: "nexo",
                table: "stored_prompt_versions",
                columns: new[] { "PromptType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_prompt_versions_PromptType_Version",
                schema: "nexo",
                table: "stored_prompt_versions",
                columns: new[] { "PromptType", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_ai_limits_TenantId",
                schema: "nexo",
                table: "tenant_ai_limits",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_providers",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "interpreter_telemetry",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "stored_prompt_versions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tenant_ai_limits",
                schema: "nexo");
        }
    }
}
