using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.TaxIntelligence;

// Platform-wide catalogue (no tenant filter): legal sources, rules, rule versions and their link.
// Tenants read it; only the platform admin API writes it, and versions are never edited in place.

internal sealed class TaxLegalSourceConfiguration : IEntityTypeConfiguration<TaxLegalSource>
{
    public void Configure(EntityTypeBuilder<TaxLegalSource> b)
    {
        b.ToTable("tax_legal_sources", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_tax_legal_sources_code");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200).IsRequired();
        b.Property(x => x.Url).HasColumnName("url").HasMaxLength(500).IsRequired();
        b.Property(x => x.Excerpt).HasColumnName("excerpt").IsRequired();
        b.Property(x => x.PublishedOn).HasColumnName("published_on");
        b.Property(x => x.TextVerification).HasColumnName("text_verification").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.VerifiedAt).HasColumnName("verified_at").HasColumnType("timestamptz");
        b.Property(x => x.VerifiedBy).HasColumnName("verified_by").HasMaxLength(200);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        b.Property(x => x.VerificationRecordedById).HasColumnName("verification_recorded_by_id");
        b.Property(x => x.VerificationRecordedBy).HasColumnName("verification_recorded_by").HasMaxLength(200);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
        b.Ignore(x => x.IsTextVerified);
    }
}

internal sealed class TaxRuleConfiguration : IEntityTypeConfiguration<TaxRule>
{
    public void Configure(EntityTypeBuilder<TaxRule> b)
    {
        b.ToTable("tax_rules", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Key).HasColumnName("key").HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.Key).IsUnique().HasDatabaseName("ux_tax_rules_key");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.RuleId)
            .HasConstraintName("fk_tax_rule_versions_rules").OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxRuleVersionConfiguration : IEntityTypeConfiguration<TaxRuleVersion>
{
    public void Configure(EntityTypeBuilder<TaxRuleVersion> b)
    {
        b.ToTable("tax_rule_versions", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.RuleId).HasColumnName("rule_id").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.HasIndex(x => new { x.RuleId, x.Version }).IsUnique().HasDatabaseName("ux_tax_rule_versions_rule_version");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        b.Property(x => x.ValidTo).HasColumnName("valid_to");
        b.HasIndex(x => new { x.Status, x.ValidFrom }).HasDatabaseName("ix_tax_rule_versions_status_valid_from");
        b.Property(x => x.Priority).HasColumnName("priority").IsRequired();
        b.Property(x => x.ConditionsJson).HasColumnName("conditions").HasColumnType("json").IsRequired();
        b.Property(x => x.ResultJson).HasColumnName("result").HasColumnType("json").IsRequired();
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.Origin).HasColumnName("origin").HasMaxLength(60).IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.ActivatedAt).HasColumnName("activated_at").HasColumnType("timestamptz");
        b.Property(x => x.StatusChangedAt).HasColumnName("status_changed_at").HasColumnType("timestamptz");
        b.Property(x => x.ProfessionalReview).HasColumnName("professional_review").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ProfessionalReviewedBy).HasColumnName("professional_reviewed_by").HasMaxLength(200);
        b.Property(x => x.ProfessionalReviewedAt).HasColumnName("professional_reviewed_at").HasColumnType("timestamptz");
        b.Property(x => x.ProfessionalReviewNotes).HasColumnName("professional_review_notes").HasMaxLength(4000);
        b.Property(x => x.ProfessionalReference).HasColumnName("professional_reference").HasMaxLength(100);
        b.Property(x => x.ProfessionalReviewScope).HasColumnName("professional_review_scope").HasMaxLength(2000);
        b.Property(x => x.ProfessionalReviewValidFrom).HasColumnName("professional_review_valid_from");
        b.Property(x => x.ProfessionalReviewValidTo).HasColumnName("professional_review_valid_to");
        b.Property(x => x.ProfessionalReviewAssumptions).HasColumnName("professional_review_assumptions").HasMaxLength(4000);
        b.Property(x => x.ProfessionalReviewCaveats).HasColumnName("professional_review_caveats").HasMaxLength(4000);
        b.Property(x => x.ProfessionalReviewRegimes).HasColumnName("professional_review_regimes").HasColumnType("text[]").IsRequired();
        b.Property(x => x.ProfessionalReviewedOn).HasColumnName("professional_reviewed_on");
        b.Property(x => x.ProfessionalReviewRecordedById).HasColumnName("professional_review_recorded_by_id");
        b.Property(x => x.ProfessionalReviewRecordedBy).HasColumnName("professional_review_recorded_by").HasMaxLength(200);
        b.Property(x => x.ProfessionalReviewRecordedByRole).HasColumnName("professional_review_recorded_by_role").HasMaxLength(60);
        b.Property(x => x.ProfessionalReviewRecordId).HasColumnName("professional_review_record_id");
        b.Property(x => x.ProfessionalReviewDocumentId).HasColumnName("professional_review_document_id");
        // Internal pilot approval: a separate dimension, never a professional review.
        b.Property(x => x.InternalApproval).HasColumnName("internal_approval").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.InternalApprovalPolicy).HasColumnName("internal_approval_policy").HasMaxLength(60);
        b.Property(x => x.InternalApprovedById).HasColumnName("internal_approved_by_id");
        b.Property(x => x.InternalApprovedBy).HasColumnName("internal_approved_by").HasMaxLength(200);
        b.Property(x => x.InternalApprovedByRole).HasColumnName("internal_approved_by_role").HasMaxLength(60);
        b.Property(x => x.InternalApprovedAt).HasColumnName("internal_approved_at").HasColumnType("timestamptz");
        b.Property(x => x.InternalApprovalScope).HasColumnName("internal_approval_scope").HasMaxLength(2000);
        b.Property(x => x.InternalApprovalValidFrom).HasColumnName("internal_approval_valid_from");
        b.Property(x => x.InternalApprovalValidTo).HasColumnName("internal_approval_valid_to");
        b.Property(x => x.InternalApprovalCaveats).HasColumnName("internal_approval_caveats").HasMaxLength(4000);
        b.Property(x => x.InternalApprovalDecisionRef).HasColumnName("internal_approval_decision_ref").HasMaxLength(300);
        b.Property(x => x.InternalApprovalWithdrawalReason).HasColumnName("internal_approval_withdrawal_reason").HasMaxLength(2000);
        b.Property(x => x.InternalApprovalWithdrawnAt).HasColumnName("internal_approval_withdrawn_at").HasColumnType("timestamptz");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasMany(x => x.Sources).WithOne().HasForeignKey(s => s.RuleVersionId)
            .HasConstraintName("fk_tax_rule_version_sources_versions").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Sources).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxRuleVersionSourceConfiguration : IEntityTypeConfiguration<TaxRuleVersionSource>
{
    public void Configure(EntityTypeBuilder<TaxRuleVersionSource> b)
    {
        b.ToTable("tax_rule_version_sources", "nexo");
        b.HasKey(x => new { x.RuleVersionId, x.LegalSourceId });
        b.Property(x => x.RuleVersionId).HasColumnName("rule_version_id");
        b.Property(x => x.LegalSourceId).HasColumnName("legal_source_id");
        b.HasOne(x => x.LegalSource).WithMany().HasForeignKey(x => x.LegalSourceId)
            .HasConstraintName("fk_tax_rule_version_sources_legal_sources").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.LegalSourceId).HasDatabaseName("ix_tax_rule_version_sources_legal_source_id");
    }
}

internal sealed class TaxPilotScopeConfiguration : IEntityTypeConfiguration<TaxPilotScope>
{
    public void Configure(EntityTypeBuilder<TaxPilotScope> b)
    {
        b.ToTable("tax_pilot_scopes", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        b.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        b.Property(x => x.ValidTo).HasColumnName("valid_to");
        b.Property(x => x.DecisionReference).HasColumnName("decision_reference").HasMaxLength(300).IsRequired();
        b.Property(x => x.ActivatedById).HasColumnName("activated_by_id").IsRequired();
        b.Property(x => x.ActivatedByName).HasColumnName("activated_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ActivatedAt).HasColumnName("activated_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasMany(x => x.Participants).WithOne().HasForeignKey(p => p.ScopeId)
            .HasConstraintName("fk_tax_pilot_participants_scopes").OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaxPilotParticipantConfiguration : IEntityTypeConfiguration<TaxPilotParticipant>
{
    public void Configure(EntityTypeBuilder<TaxPilotParticipant> b)
    {
        b.ToTable("tax_pilot_participants", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ScopeId).HasColumnName("scope_id").IsRequired();
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.StoreId).HasColumnName("store_id");
        b.HasIndex(x => new { x.ScopeId, x.TenantId }).HasDatabaseName("ix_tax_pilot_participants_scope_tenant");
        b.Property(x => x.AddedById).HasColumnName("added_by_id").IsRequired();
        b.Property(x => x.AddedByName).HasColumnName("added_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.AddedAt).HasColumnName("added_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.RemovedAt).HasColumnName("removed_at").HasColumnType("timestamptz");
        b.Property(x => x.RemovedById).HasColumnName("removed_by_id");
        b.Property(x => x.RemovedByName).HasColumnName("removed_by_name").HasMaxLength(200);
        b.Property(x => x.RemovalReason).HasColumnName("removal_reason").HasMaxLength(1000);
        b.Ignore(x => x.IsActive);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
    }
}
