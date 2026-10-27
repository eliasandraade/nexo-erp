using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.TaxIntelligence;

// Establishment-scoped tax data: every table carries tenant_id and store_id and is filtered by both
// (StoreEntity global query filter). Money is numeric(18,2), rates numeric(9,4) — never float.

internal static class TaxColumns
{
    public static PropertyBuilder<decimal> Money(this PropertyBuilder<decimal> p, string column)
        => p.HasColumnName(column).HasColumnType("numeric(18,2)").IsRequired();
}

internal sealed class TaxProfileConfiguration : IEntityTypeConfiguration<TaxProfile>
{
    public void Configure(EntityTypeBuilder<TaxProfile> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_profiles");
        b.HasIndex(x => new { x.TenantId, x.StoreId }).IsUnique().HasDatabaseName("ux_tax_profiles_tenant_store");
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ProfileId)
            .HasConstraintName("fk_tax_profile_versions_profiles").OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxProfileVersionConfiguration : IEntityTypeConfiguration<TaxProfileVersion>
{
    public void Configure(EntityTypeBuilder<TaxProfileVersion> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_profile_versions");
        b.Property(x => x.ProfileId).HasColumnName("profile_id").IsRequired();
        b.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        b.HasIndex(x => new { x.ProfileId, x.VersionNumber }).IsUnique().HasDatabaseName("ux_tax_profile_versions_profile_number");
        b.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.ValidFrom }).HasDatabaseName("ix_tax_profile_versions_tenant_store_valid_from");
        b.Property(x => x.Regime).HasColumnName("regime").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.State).HasColumnName("state").HasMaxLength(2).IsRequired();
        b.Property(x => x.CityIbgeCode).HasColumnName("city_ibge_code").HasMaxLength(7).IsRequired();
        b.Property(x => x.PrimaryCnae).HasColumnName("primary_cnae").HasMaxLength(7).IsRequired();
        b.Property(x => x.SecondaryCnaes).HasColumnName("secondary_cnaes").HasColumnType("text[]").IsRequired();
        b.Property(x => x.SpecialRegimes).HasColumnName("special_regimes").HasColumnType("text[]").IsRequired();
        b.Property(x => x.Benefits).HasColumnName("benefits").HasColumnType("text[]").IsRequired();
        b.Property(x => x.AccessoryObligationsCompliant).HasColumnName("accessory_obligations_compliant");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        b.Property(x => x.CreatedById).HasColumnName("created_by_id").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();
        b.Ignore(x => x.IsSimplesNacional);
    }
}

internal sealed class TaxTransactionConfiguration : IEntityTypeConfiguration<TaxTransaction>
{
    public void Configure(EntityTypeBuilder<TaxTransaction> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_transactions");
        b.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.SourceId).HasColumnName("source_id");
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.Source, x.SourceId }).HasDatabaseName("ix_tax_transactions_source");
        b.Property(x => x.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.OperationDate).HasColumnName("operation_date").IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(300);
        b.Property(x => x.CounterpartyRegime).HasColumnName("counterparty_regime").HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.CounterpartyState).HasColumnName("counterparty_state").HasMaxLength(2);
        b.Property(x => x.FactsHash).HasColumnName("facts_hash").HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.FactsHash }).HasDatabaseName("ix_tax_transactions_facts_hash");
        b.Property(x => x.CreatedById).HasColumnName("created_by_id").IsRequired();

        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.TransactionId)
            .HasConstraintName("fk_tax_transaction_lines_transactions").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxTransactionLineConfiguration : IEntityTypeConfiguration<TaxTransactionLine>
{
    public void Configure(EntityTypeBuilder<TaxTransactionLine> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_transaction_lines");
        b.Property(x => x.TransactionId).HasColumnName("transaction_id").IsRequired();
        b.Property(x => x.LineIndex).HasColumnName("line_index").IsRequired();
        b.HasIndex(x => new { x.TransactionId, x.LineIndex }).IsUnique().HasDatabaseName("ux_tax_transaction_lines_transaction_index");
        b.Property(x => x.ItemKind).HasColumnName("item_kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(300).IsRequired();
        b.Property(x => x.Ncm).HasColumnName("ncm").HasMaxLength(8);
        b.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)").IsRequired();
        b.Property(x => x.GrossAmount).Money("gross_amount");
        b.Property(x => x.UnconditionalDiscount).Money("unconditional_discount");
        b.Property(x => x.ConditionalDiscount).Money("conditional_discount");
        b.Property(x => x.Surcharges).Money("surcharges");
        b.Property(x => x.ExcludedTaxes).Money("excluded_taxes");
        b.Property(x => x.ExcludedTaxesInformed).HasColumnName("excluded_taxes_informed").IsRequired();
        b.Property(x => x.SpecialTreatment).HasColumnName("special_treatment").HasMaxLength(60);
    }
}

internal sealed class TaxCalculationConfiguration : IEntityTypeConfiguration<TaxCalculation>
{
    public void Configure(EntityTypeBuilder<TaxCalculation> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_calculations");
        b.Property(x => x.TransactionId).HasColumnName("transaction_id").IsRequired();
        b.HasOne<TaxTransaction>().WithMany().HasForeignKey(x => x.TransactionId)
            .HasConstraintName("fk_tax_calculations_transactions").OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ProfileVersionId).HasColumnName("profile_version_id").IsRequired();
        b.HasOne<TaxProfileVersion>().WithMany().HasForeignKey(x => x.ProfileVersionId)
            .HasConstraintName("fk_tax_calculations_profile_versions").OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ReferenceDate).HasColumnName("reference_date").IsRequired();
        b.Property(x => x.EngineVersion).HasColumnName("engine_version").HasMaxLength(60).IsRequired();
        b.Property(x => x.InputJson).HasColumnName("input").HasColumnType("json").IsRequired();
        b.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.RuleSetHash).HasColumnName("rule_set_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.CalculationKey).HasColumnName("calculation_key").HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.CalculationKey }).IsUnique().HasDatabaseName("ux_tax_calculations_key");
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.CreatedAt }).HasDatabaseName("ix_tax_calculations_tenant_store_created");
        b.Property(x => x.OutputJson).HasColumnName("output").HasColumnType("json").IsRequired();
        b.Property(x => x.OutputHash).HasColumnName("output_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.TotalBase).Money("total_base");
        b.Property(x => x.TotalTax).Money("total_tax");
        b.Property(x => x.CreatedById).HasColumnName("created_by_id").IsRequired();

        b.HasMany(x => x.Components).WithOne().HasForeignKey(c => c.CalculationId)
            .HasConstraintName("fk_tax_components_calculations").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Components).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Executions).WithOne().HasForeignKey(e => e.CalculationId)
            .HasConstraintName("fk_tax_rule_executions_calculations").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Executions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxComponentConfiguration : IEntityTypeConfiguration<TaxComponent>
{
    public void Configure(EntityTypeBuilder<TaxComponent> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_components");
        b.Property(x => x.CalculationId).HasColumnName("calculation_id").IsRequired();
        b.HasIndex(x => x.CalculationId).HasDatabaseName("ix_tax_components_calculation_id");
        b.Property(x => x.LineIndex).HasColumnName("line_index").IsRequired();
        b.Property(x => x.Tax).HasColumnName("tax").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.BaseAmount).Money("base_amount");
        b.Property(x => x.RatePercent).HasColumnName("rate_percent").HasColumnType("numeric(9,4)").IsRequired();
        b.Property(x => x.Amount).Money("amount");
        b.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").IsRequired();
        b.HasOne<TaxRuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId)
            .HasConstraintName("fk_tax_components_rule_versions").OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaxRuleExecutionConfiguration : IEntityTypeConfiguration<TaxRuleExecution>
{
    public void Configure(EntityTypeBuilder<TaxRuleExecution> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_rule_executions");
        b.Property(x => x.CalculationId).HasColumnName("calculation_id").IsRequired();
        b.HasIndex(x => x.CalculationId).HasDatabaseName("ix_tax_rule_executions_calculation_id");
        b.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").IsRequired();
        b.HasOne<TaxRuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId)
            .HasConstraintName("fk_tax_rule_executions_rule_versions").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.RuleVersionId).HasDatabaseName("ix_tax_rule_executions_rule_version_id");
        b.Property(x => x.RuleKey).HasColumnName("rule_key").HasMaxLength(80).IsRequired();
        b.Property(x => x.RuleVersion).HasColumnName("rule_version").IsRequired();
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.LineIndex).HasColumnName("line_index");
        b.Property(x => x.Matched).HasColumnName("matched").IsRequired();
        b.Property(x => x.ConditionsJson).HasColumnName("conditions").HasColumnType("json").IsRequired();
        b.Property(x => x.OutputJson).HasColumnName("output").HasMaxLength(500).IsRequired();
        b.Property(x => x.LegalSourceCodes).HasColumnName("legal_source_codes").HasMaxLength(500).IsRequired();
    }
}

internal sealed class TaxSimulationConfiguration : IEntityTypeConfiguration<TaxSimulation>
{
    public void Configure(EntityTypeBuilder<TaxSimulation> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_simulations");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.EngineVersion).HasColumnName("engine_version").HasMaxLength(60).IsRequired();
        b.Property(x => x.CreatedById).HasColumnName("created_by_id").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.CreatedAt }).HasDatabaseName("ix_tax_simulations_tenant_store_created");
        b.HasMany(x => x.Scenarios).WithOne().HasForeignKey(s => s.SimulationId)
            .HasConstraintName("fk_tax_simulation_scenarios_simulations").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Scenarios).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxSimulationScenarioConfiguration : IEntityTypeConfiguration<TaxSimulationScenario>
{
    public void Configure(EntityTypeBuilder<TaxSimulationScenario> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_simulation_scenarios");
        b.Property(x => x.SimulationId).HasColumnName("simulation_id").IsRequired();
        b.Property(x => x.ScenarioIndex).HasColumnName("scenario_index").IsRequired();
        b.HasIndex(x => new { x.SimulationId, x.ScenarioIndex }).IsUnique().HasDatabaseName("ux_tax_simulation_scenarios_simulation_index");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.IsBaseline).HasColumnName("is_baseline").IsRequired();
        b.Property(x => x.InputJson).HasColumnName("input").HasColumnType("json").IsRequired();
        b.Property(x => x.ResultJson).HasColumnName("result").HasColumnType("json").IsRequired();
        b.Property(x => x.TotalTax).Money("total_tax");
        b.Property(x => x.NetCost).Money("net_cost");
    }
}

internal sealed class TaxOpportunityConfiguration : IEntityTypeConfiguration<TaxOpportunity>
{
    public void Configure(EntityTypeBuilder<TaxOpportunity> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_opportunities");
        b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.Status }).HasDatabaseName("ix_tax_opportunities_tenant_store_status");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
        b.Property(x => x.DetectorKey).HasColumnName("detector_key").HasMaxLength(80).IsRequired();
        b.Property(x => x.SourceCalculationId).HasColumnName("source_calculation_id");
        b.HasOne<TaxCalculation>().WithMany().HasForeignKey(x => x.SourceCalculationId)
            .HasConstraintName("fk_tax_opportunities_calculations").OnDelete(DeleteBehavior.Restrict);
        // One opportunity per detector and calculation: running detection twice does not duplicate it.
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.DetectorKey, x.SourceCalculationId }).IsUnique()
            .HasDatabaseName("ux_tax_opportunities_detector_calculation");
        b.Property(x => x.CurrentScenarioJson).HasColumnName("current_scenario").HasColumnType("json").IsRequired();
        b.Property(x => x.ProposedScenarioJson).HasColumnName("proposed_scenario").HasColumnType("json").IsRequired();
        b.Property(x => x.EstimatedImpact).Money("estimated_impact");
        b.Property(x => x.Risk).HasColumnName("risk").HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(x => x.Confidence).HasColumnName("confidence").IsRequired();
        b.Property(x => x.LegalSourceCodes).HasColumnName("legal_source_codes").HasMaxLength(500).IsRequired();
        b.Property(x => x.Caveats).HasColumnName("caveats").HasMaxLength(2000);
        b.Property(x => x.BlockedAs).HasColumnName("blocked_as").HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.BlockReason).HasColumnName("block_reason").HasMaxLength(1000);
        b.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        b.Property(x => x.ReviewedById).HasColumnName("reviewed_by_id");
        b.Property(x => x.ReviewedByName).HasColumnName("reviewed_by_name").HasMaxLength(200);
        b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamptz");
        b.Ignore(x => x.ImpactNature);

        b.HasMany(x => x.Requirements).WithOne().HasForeignKey(r => r.OpportunityId)
            .HasConstraintName("fk_tax_opportunity_requirements_opportunities").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Requirements).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Evidence).WithOne().HasForeignKey(e => e.OpportunityId)
            .HasConstraintName("fk_tax_opportunity_evidence_opportunities").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Evidence).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxOpportunityRequirementConfiguration : IEntityTypeConfiguration<TaxOpportunityRequirement>
{
    public void Configure(EntityTypeBuilder<TaxOpportunityRequirement> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_opportunity_requirements");
        b.Property(x => x.OpportunityId).HasColumnName("opportunity_id").IsRequired();
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        b.HasIndex(x => new { x.OpportunityId, x.Code }).IsUnique().HasDatabaseName("ux_tax_opportunity_requirements_opportunity_code");
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(500).IsRequired();
        b.Property(x => x.LegalSourceCode).HasColumnName("legal_source_code").HasMaxLength(60);
        b.Property(x => x.Met).HasColumnName("met");
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        b.Property(x => x.AnsweredById).HasColumnName("answered_by_id");
        b.Property(x => x.AnsweredByName).HasColumnName("answered_by_name").HasMaxLength(200);
        b.Property(x => x.AnsweredAt).HasColumnName("answered_at").HasColumnType("timestamptz");
    }
}

internal sealed class TaxOpportunityEvidenceConfiguration : IEntityTypeConfiguration<TaxOpportunityEvidence>
{
    public void Configure(EntityTypeBuilder<TaxOpportunityEvidence> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_opportunity_evidence");
        b.Property(x => x.OpportunityId).HasColumnName("opportunity_id").IsRequired();
        b.HasIndex(x => x.OpportunityId).HasDatabaseName("ix_tax_opportunity_evidence_opportunity_id");
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
        b.Property(x => x.DocumentReference).HasColumnName("document_reference").HasMaxLength(200);
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.AddedById).HasColumnName("added_by_id").IsRequired();
        b.Property(x => x.AddedByName).HasColumnName("added_by_name").HasMaxLength(200).IsRequired();
    }
}

internal sealed class TaxDecisionConfiguration : IEntityTypeConfiguration<TaxDecision>
{
    public void Configure(EntityTypeBuilder<TaxDecision> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_decisions");
        b.Property(x => x.OpportunityId).HasColumnName("opportunity_id").IsRequired();
        b.HasIndex(x => x.OpportunityId).IsUnique().HasDatabaseName("ux_tax_decisions_opportunity_id");
        b.HasOne<TaxOpportunity>().WithMany().HasForeignKey(x => x.OpportunityId)
            .HasConstraintName("fk_tax_decisions_opportunities").OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Strategy).HasColumnName("strategy").HasMaxLength(200).IsRequired();
        b.Property(x => x.DossierJson).HasColumnName("dossier").HasColumnType("json").IsRequired();
        b.Property(x => x.DossierHash).HasColumnName("dossier_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.Risk).HasColumnName("risk").HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(x => x.HighRiskAcknowledged).HasColumnName("high_risk_acknowledged").IsRequired();
        b.Property(x => x.EngineVersion).HasColumnName("engine_version").HasMaxLength(60).IsRequired();
        b.Property(x => x.ApprovedById).HasColumnName("approved_by_id").IsRequired();
        b.Property(x => x.ApprovedByName).HasColumnName("approved_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamptz").IsRequired();
    }
}

internal sealed class TaxCreditConfiguration : IEntityTypeConfiguration<TaxCredit>
{
    public void Configure(EntityTypeBuilder<TaxCredit> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_credits");
        b.Property(x => x.Tax).HasColumnName("tax").HasConversion<string>().HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.StoreId, x.Tax }).HasDatabaseName("ix_tax_credits_tenant_store_tax");
        b.Property(x => x.SourceDocumentType).HasColumnName("source_document_type").HasMaxLength(40).IsRequired();
        b.Property(x => x.SourceDocumentKey).HasColumnName("source_document_key").HasMaxLength(44);
        b.Property(x => x.SupplierDocument).HasColumnName("supplier_document").HasMaxLength(14);
        b.Property(x => x.AcquiredOn).HasColumnName("acquired_on").IsRequired();
        b.Property(x => x.ExpiresOn).HasColumnName("expires_on");
        b.Property(x => x.OriginalAmount).Money("original_amount");
        b.Property(x => x.EligibleAmount).Money("eligible_amount");
        b.Property(x => x.EligibilityBasis).HasColumnName("eligibility_basis").HasMaxLength(500).IsRequired();
        b.Property(x => x.SourceCalculationId).HasColumnName("source_calculation_id");
        b.HasOne<TaxCalculation>().WithMany().HasForeignKey(x => x.SourceCalculationId)
            .HasConstraintName("fk_tax_credits_calculations").OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Entries).WithOne().HasForeignKey(e => e.CreditId)
            .HasConstraintName("fk_tax_credit_ledger_entries_credits").OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxCreditLedgerEntryConfiguration : IEntityTypeConfiguration<TaxCreditLedgerEntry>
{
    public void Configure(EntityTypeBuilder<TaxCreditLedgerEntry> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_credit_ledger_entries");
        b.Property(x => x.CreditId).HasColumnName("credit_id").IsRequired();
        b.HasIndex(x => x.CreditId).HasDatabaseName("ix_tax_credit_ledger_entries_credit_id");
        b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Amount).Money("amount");
        b.Property(x => x.ObligationReference).HasColumnName("obligation_reference").HasMaxLength(100);
        // The database also refuses using one credit twice in the same obligation.
        b.HasIndex(x => new { x.CreditId, x.ObligationReference }).IsUnique()
            .HasFilter("type = 'Used'").HasDatabaseName("ux_tax_credit_ledger_entries_credit_obligation");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        b.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired();
        b.Property(x => x.ActorName).HasColumnName("actor_name").HasMaxLength(200).IsRequired();
    }
}

internal sealed class SplitExpectationConfiguration : IEntityTypeConfiguration<SplitExpectation>
{
    public void Configure(EntityTypeBuilder<SplitExpectation> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_split_expectations");
        b.Property(x => x.CalculationId).HasColumnName("calculation_id").IsRequired();
        b.HasOne<TaxCalculation>().WithMany().HasForeignKey(x => x.CalculationId)
            .HasConstraintName("fk_tax_split_expectations_calculations").OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Tax).HasColumnName("tax").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.CalculationId, x.Tax }).IsUnique().HasDatabaseName("ux_tax_split_expectations_calculation_tax");
        b.Property(x => x.Procedure).HasColumnName("procedure").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ExpectedAmount).Money("expected_amount");
    }
}

internal sealed class SplitSettlementConfiguration : IEntityTypeConfiguration<SplitSettlement>
{
    public void Configure(EntityTypeBuilder<SplitSettlement> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_split_settlements");
        b.Property(x => x.ExpectationId).HasColumnName("expectation_id").IsRequired();
        b.HasOne<SplitExpectation>().WithMany().HasForeignKey(x => x.ExpectationId)
            .HasConstraintName("fk_tax_split_settlements_expectations").OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ActualAmount).Money("actual_amount");
        b.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.ExpectationId, x.ExternalReference }).IsUnique().HasDatabaseName("ux_tax_split_settlements_expectation_reference");
        b.Property(x => x.SettledAt).HasColumnName("settled_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.RecordedById).HasColumnName("recorded_by_id").IsRequired();
    }
}
