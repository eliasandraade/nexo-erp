using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only migration: retires the per-vertical Service SKUs sold before the v1.1
    /// single-module model.
    ///
    /// Runs as a migration (not in the seeder) on purpose — Program.cs only calls
    /// DataSeeder.SeedAsync() outside Production, so a seeder step would never execute
    /// against the production database, while MigrateAsync() runs in every environment.
    ///
    /// Order matters: the per-store preset is captured from the legacy key FIRST, so a
    /// converted tenant keeps the exact vertical it was already using instead of being
    /// bounced back to onboarding. Only then is the subscription rewritten to "service"
    /// and the legacy rows removed.
    ///
    /// Idempotent by construction: every statement is a no-op when no legacy row exists.
    /// </summary>
    public partial class ConvertLegacyServiceSubscriptions : Migration
    {
        private const string LegacyKeys =
            "'clinica-medica','salao-beleza','pet-shop','oficina-mecanica','nutricionista'," +
            "'personal-trainer','autoescola','escola-idiomas','programador-autonomo'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Preserve the vertical: each store of a legacy tenant gets svc_settings
            //    carrying the legacy key as its internal preset, unless it already has one.
            //    DISTINCT ON picks a single key deterministically for the (rare) tenant that
            //    somehow holds more than one.
            migrationBuilder.Sql($@"
                WITH legacy AS (
                    SELECT DISTINCT ON (ms.tenant_id) ms.tenant_id, ms.module_key
                    FROM nexo.module_subscriptions ms
                    WHERE ms.module_key IN ({LegacyKeys})
                    ORDER BY ms.tenant_id, ms.module_key
                )
                INSERT INTO nexo.svc_settings (id, tenant_id, store_id, preset_key, created_at, updated_at)
                SELECT gen_random_uuid(), l.tenant_id, st.id, l.module_key, now(), now()
                FROM legacy l
                JOIN nexo.stores st ON st.tenant_id = l.tenant_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM nexo.svc_settings s
                    WHERE s.tenant_id = l.tenant_id AND s.store_id = st.id
                );");

            // 2. Convert one legacy subscription per tenant into the single 'service' module,
            //    keeping status, plan and billing period intact. Skipped for tenants that
            //    already hold 'service' (the unique index on (tenant_id, module_key) would
            //    reject the duplicate).
            migrationBuilder.Sql($@"
                UPDATE nexo.module_subscriptions
                SET module_key = 'service', updated_at = now()
                WHERE id IN (
                    SELECT DISTINCT ON (m.tenant_id) m.id
                    FROM nexo.module_subscriptions m
                    WHERE m.module_key IN ({LegacyKeys})
                      AND NOT EXISTS (
                          SELECT 1 FROM nexo.module_subscriptions x
                          WHERE x.tenant_id = m.tenant_id AND x.module_key = 'service'
                      )
                    ORDER BY m.tenant_id, m.module_key
                );");

            // 3. Drop whatever legacy rows remain — those tenants now hold 'service'.
            migrationBuilder.Sql(
                $"DELETE FROM nexo.module_subscriptions WHERE module_key IN ({LegacyKeys});");

            // 4. The per-vertical SKUs stop being sellable. Nothing references
            //    module_definitions by FK, and step 3 left no subscription on these keys.
            migrationBuilder.Sql(
                $"DELETE FROM nexo.module_definitions WHERE key IN ({LegacyKeys});");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. The original per-vertical key of a converted tenant is not
            // recoverable from the post-migration state, so a reversal would have to invent
            // data. Rolling back means restoring from backup.
        }
    }
}
