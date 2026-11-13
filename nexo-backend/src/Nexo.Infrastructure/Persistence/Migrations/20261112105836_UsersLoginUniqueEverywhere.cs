using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsersLoginUniqueEverywhere : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sign-in receives a login and no company, so a login can only mean one account. This migration only
            // adds the index: it changes no row, renames nobody and chooses no winner. Over repeated logins it
            // stops here, before the index, with a message that names no account.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM nexo.users GROUP BY login HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'nexo.users has repeated logins: resolve them before this migration, which changes no row';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_users_login",
                schema: "nexo",
                table: "users",
                column: "login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_login",
                schema: "nexo",
                table: "users");
        }
    }
}
