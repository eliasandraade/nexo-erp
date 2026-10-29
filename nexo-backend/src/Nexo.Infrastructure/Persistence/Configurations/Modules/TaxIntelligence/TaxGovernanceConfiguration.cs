using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Common;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.TaxIntelligence;

// Governance, documents and catalogues added to finish the module: the runtime state of the module, the history
// of professional reviews, materiality and retention policies, private files and the classification and
// obligation catalogues. Platform tables carry no tenant filter; company tables are tenant- or store-scoped.

internal static class TaxAuditColumns
{
    public static void KeyAndTimestamps<T>(this EntityTypeBuilder<T> b, string table) where T : BaseEntity
    {
        b.ToTable(table, "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
    }
}

internal sealed class TaxModuleActivationConfiguration : IEntityTypeConfiguration<TaxModuleActivation>
{
    public void Configure(EntityTypeBuilder<TaxModuleActivation> b)
    {
        b.KeyAndTimestamps("tax_module_activations");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.DecisionReference).HasColumnName("decision_reference").HasMaxLength(300).IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
        b.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired();
        b.Property(x => x.ActorName).HasColumnName("actor_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ActorRole).HasColumnName("actor_role").HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_tax_module_activations_created_at");
    }
}

internal sealed class TaxProfessionalReviewRecordConfiguration : IEntityTypeConfiguration<TaxProfessionalReviewRecord>
{
    public void Configure(EntityTypeBuilder<TaxProfessionalReviewRecord> b)
    {
        b.KeyAndTimestamps("tax_professional_review_records");
        b.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").IsRequired();
        b.HasOne<TaxRuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId)
            .HasConstraintName("fk_tax_professional_review_records_versions").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RuleVersionId, x.CreatedAt }).HasDatabaseName("ix_tax_professional_review_records_version");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.PreviousStatus).HasColumnName("previous_status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Reviewer).HasColumnName("reviewer").HasMaxLength(200).IsRequired();
        b.Property(x => x.ProfessionalReference).HasColumnName("professional_reference").HasMaxLength(100);
        b.Property(x => x.ReviewedOn).HasColumnName("reviewed_on");
        b.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(2000);
        b.Property(x => x.CoveredRegimes).HasColumnName("covered_regimes").HasColumnType("text[]").IsRequired();
        b.Property(x => x.ValidFrom).HasColumnName("valid_from");
        b.Property(x => x.ValidTo).HasColumnName("valid_to");
        b.Property(x => x.Assumptions).HasColumnName("assumptions").HasMaxLength(4000);
        b.Property(x => x.Caveats).HasColumnName("caveats").HasMaxLength(4000);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        b.Property(x => x.SupportingDocumentId).HasColumnName("supporting_document_id");
        b.HasOne<TaxPlatformDocument>().WithMany().HasForeignKey(x => x.SupportingDocumentId)
            .HasConstraintName("fk_tax_professional_review_records_documents").OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.RecordedById).HasColumnName("recorded_by_id").IsRequired();
        b.Property(x => x.RecordedByName).HasColumnName("recorded_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.RecordedByRole).HasColumnName("recorded_by_role").HasMaxLength(60).IsRequired();
    }
}

internal sealed class TaxMaterialityPolicyConfiguration : IEntityTypeConfiguration<TaxMaterialityPolicy>
{
    public void Configure(EntityTypeBuilder<TaxMaterialityPolicy> b)
    {
        b.ConfigureTenantScopedTaxEntity("tax_materiality_policies");
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Version }).IsUnique().HasDatabaseName("ux_tax_materiality_policies_tenant_version");
        b.Property(x => x.Threshold).Money("threshold");
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        b.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        b.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(2000).IsRequired();
        b.Property(x => x.DecisionReference).HasColumnName("decision_reference").HasMaxLength(300).IsRequired();
        b.Property(x => x.ConfiguredById).HasColumnName("configured_by_id").IsRequired();
        b.Property(x => x.ConfiguredByName).HasColumnName("configured_by_name").HasMaxLength(200).IsRequired();
    }
}

internal sealed class TaxRetentionPolicyConfiguration : IEntityTypeConfiguration<TaxRetentionPolicy>
{
    public void Configure(EntityTypeBuilder<TaxRetentionPolicy> b)
    {
        b.KeyAndTimestamps("tax_retention_policies");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.HasOne<Nexo.Domain.Entities.Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_tax_retention_policies_tenants").OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.Property(x => x.EvidenceType).HasColumnName("evidence_type").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RetentionMonths).HasColumnName("retention_months").IsRequired();
        b.Property(x => x.StartsAt).HasColumnName("starts_at").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        b.Property(x => x.AllowTenantOverride).HasColumnName("allow_tenant_override").IsRequired();
        b.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(2000).IsRequired();
        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(500).IsRequired();
        b.Property(x => x.ConfiguredById).HasColumnName("configured_by_id").IsRequired();
        b.Property(x => x.ConfiguredByName).HasColumnName("configured_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.SupersededAt).HasColumnName("superseded_at").HasColumnType("timestamptz");
        b.Ignore(x => x.IsCurrent);
        b.HasIndex(x => new { x.TenantId, x.EvidenceType, x.SupersededAt }).HasDatabaseName("ix_tax_retention_policies_scope");
    }
}

internal sealed class TaxEvidenceFileConfiguration : IEntityTypeConfiguration<TaxEvidenceFile>
{
    public void Configure(EntityTypeBuilder<TaxEvidenceFile> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_evidence_files");
        b.Property(x => x.Owner).HasColumnName("owner").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Owner, x.OwnerId }).HasDatabaseName("ix_tax_evidence_files_owner");
        b.Property(x => x.RequirementId).HasColumnName("requirement_id");
        b.Property(x => x.EvidenceType).HasColumnName("evidence_type").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(400).IsRequired();
        b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("ux_tax_evidence_files_storage_key");
        b.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.Extension).HasColumnName("extension").HasMaxLength(10).IsRequired();
        b.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        b.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
        b.Property(x => x.ScanStatus).HasColumnName("scan_status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ScanEngine).HasColumnName("scan_engine").HasMaxLength(100);
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.Property(x => x.ReplacesFileId).HasColumnName("replaces_file_id");
        b.Property(x => x.ReplacedByFileId).HasColumnName("replaced_by_file_id");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.UploadedById).HasColumnName("uploaded_by_id").IsRequired();
        b.Property(x => x.UploadedByName).HasColumnName("uploaded_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
        b.Property(x => x.RevokedById).HasColumnName("revoked_by_id");
        b.Property(x => x.RevokedByName).HasColumnName("revoked_by_name").HasMaxLength(200);
        b.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(1000);
        b.Property(x => x.LegalHold).HasColumnName("legal_hold").IsRequired();
        b.Property(x => x.LegalHoldReason).HasColumnName("legal_hold_reason").HasMaxLength(1000);
        b.Property(x => x.LegalHoldChangedAt).HasColumnName("legal_hold_changed_at").HasColumnType("timestamptz");
        b.Property(x => x.LegalHoldChangedByName).HasColumnName("legal_hold_changed_by_name").HasMaxLength(200);
    }
}

internal sealed class TaxPlatformDocumentConfiguration : IEntityTypeConfiguration<TaxPlatformDocument>
{
    public void Configure(EntityTypeBuilder<TaxPlatformDocument> b)
    {
        b.KeyAndTimestamps("tax_platform_documents");
        b.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(40).IsRequired();
        b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(400).IsRequired();
        b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("ux_tax_platform_documents_storage_key");
        b.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.Extension).HasColumnName("extension").HasMaxLength(10).IsRequired();
        b.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        b.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
        b.Property(x => x.ScanStatus).HasColumnName("scan_status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.UploadedById).HasColumnName("uploaded_by_id").IsRequired();
        b.Property(x => x.UploadedByName).HasColumnName("uploaded_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
        b.Property(x => x.RevokedByName).HasColumnName("revoked_by_name").HasMaxLength(200);
        b.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(1000);
    }
}

internal sealed class TaxClassificationCodeConfiguration : IEntityTypeConfiguration<TaxClassificationCode>
{
    public void Configure(EntityTypeBuilder<TaxClassificationCode> b)
    {
        b.KeyAndTimestamps("tax_classification_codes");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        b.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        b.HasIndex(x => new { x.Kind, x.Code, x.ValidFrom }).IsUnique().HasDatabaseName("ux_tax_classification_codes_kind_code_from");
        b.Property(x => x.ValidTo).HasColumnName("valid_to");
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(500).IsRequired();
        b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(100);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ProfessionalReview).HasColumnName("professional_review").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ProfessionalReviewNotes).HasColumnName("professional_review_notes").HasMaxLength(2000);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.ImportBatchId).HasColumnName("import_batch_id");
        b.Property(x => x.SourceDocumentId).HasColumnName("source_document_id");
        b.HasOne<TaxPlatformDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId)
            .HasConstraintName("fk_tax_classification_codes_documents").OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.StatusChangedByName).HasColumnName("status_changed_by_name").HasMaxLength(200);
        b.Property(x => x.StatusChangedAt).HasColumnName("status_changed_at").HasColumnType("timestamptz");
        b.Ignore(x => x.IsProfessionallyApproved);
    }
}

internal sealed class TaxItemClassificationConfiguration : IEntityTypeConfiguration<TaxItemClassification>
{
    public void Configure(EntityTypeBuilder<TaxItemClassification> b)
    {
        b.ConfigureTenantScopedTaxEntity("tax_item_classifications");
        b.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.ProductId }).IsUnique().HasDatabaseName("ux_tax_item_classifications_tenant_product");
        b.Property(x => x.ItemKind).HasColumnName("item_kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Ncm).HasColumnName("ncm").HasMaxLength(8);
        b.Property(x => x.Nbs).HasColumnName("nbs").HasMaxLength(12);
        b.Property(x => x.CClassTrib).HasColumnName("c_class_trib").HasMaxLength(10).IsRequired();
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        b.Property(x => x.SetById).HasColumnName("set_by_id").IsRequired();
        b.Property(x => x.SetByName).HasColumnName("set_by_name").HasMaxLength(200).IsRequired();
    }
}

internal sealed class TaxObligationDefinitionConfiguration : IEntityTypeConfiguration<TaxObligationDefinition>
{
    public void Configure(EntityTypeBuilder<TaxObligationDefinition> b)
    {
        b.KeyAndTimestamps("tax_obligation_definitions");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_tax_obligation_definitions_code");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(500).IsRequired();
        b.Property(x => x.Active).HasColumnName("active").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();
    }
}

internal sealed class TaxComplianceObligationResultConfiguration : IEntityTypeConfiguration<TaxComplianceObligationResult>
{
    public void Configure(EntityTypeBuilder<TaxComplianceObligationResult> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("tax_compliance_obligation_results");
        b.Property(x => x.AssessmentId).HasColumnName("assessment_id").IsRequired();
        b.HasIndex(x => new { x.AssessmentId, x.ObligationCode }).IsUnique().HasDatabaseName("ux_tax_compliance_obligation_results_obligation");
        b.Property(x => x.ObligationCode).HasColumnName("obligation_code").HasMaxLength(60).IsRequired();
        b.Property(x => x.ObligationName).HasColumnName("obligation_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(500);
        b.Property(x => x.EvidenceFileId).HasColumnName("evidence_file_id");
        b.Property(x => x.Remediation).HasColumnName("remediation").HasMaxLength(2000);
        b.Property(x => x.RemediationVerifiedOn).HasColumnName("remediation_verified_on");
        b.Property(x => x.RemediationVerifiedBy).HasColumnName("remediation_verified_by").HasMaxLength(200);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
    }
}

internal static class TaxTenantScoped
{
    /// <summary>Tenant-scoped tax table: key, tenant column + FK and audit columns (filtered by tenant).</summary>
    public static void ConfigureTenantScopedTaxEntity<T>(this EntityTypeBuilder<T> b, string table) where T : TenantEntity
    {
        b.ToTable(table, "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
        b.HasOne<Nexo.Domain.Entities.Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName($"fk_{table}_tenants").OnDelete(DeleteBehavior.Cascade);
    }
}
