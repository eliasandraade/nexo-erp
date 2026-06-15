using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeProcessedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stripe_processed_events",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stripe_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stripe_processed_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stripe_processed_events_stripe_event_id",
                schema: "nexo",
                table: "stripe_processed_events",
                column: "stripe_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stripe_processed_events",
                schema: "nexo");
        }
    }
}
