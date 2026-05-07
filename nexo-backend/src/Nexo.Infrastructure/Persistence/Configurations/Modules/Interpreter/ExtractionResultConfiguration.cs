using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class ExtractionResultConfiguration : IEntityTypeConfiguration<ExtractionResult>
{
    public void Configure(EntityTypeBuilder<ExtractionResult> builder)
    {
        builder.ToTable("int_extraction_results", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.MovementId).HasColumnName("movement_id").IsRequired();
        builder.Property(x => x.InputSource).HasColumnName("input_source").HasConversion<int>().IsRequired();
        builder.Property(x => x.RawUserText).HasColumnName("raw_user_text").HasMaxLength(1000);

        builder.Property(x => x.DetectedAmount).HasColumnName("detected_amount").HasColumnType("numeric(18,2)");
        builder.Property(x => x.AmountConfidence).HasColumnName("amount_confidence");
        builder.Property(x => x.AmountStatus).HasColumnName("amount_status").HasConversion<int>().IsRequired();

        builder.Property(x => x.DetectedDate).HasColumnName("detected_date");
        builder.Property(x => x.DateConfidence).HasColumnName("date_confidence");
        builder.Property(x => x.DateStatus).HasColumnName("date_status").HasConversion<int>().IsRequired();

        builder.Property(x => x.DetectedPayee).HasColumnName("detected_payee").HasMaxLength(255);
        builder.Property(x => x.PayeeConfidence).HasColumnName("payee_confidence");
        builder.Property(x => x.PayeeStatus).HasColumnName("payee_status").HasConversion<int>().IsRequired();

        builder.Property(x => x.DetectedAccount).HasColumnName("detected_account").HasMaxLength(255);
        builder.Property(x => x.AccountConfidence).HasColumnName("account_confidence");
        builder.Property(x => x.AccountStatus).HasColumnName("account_status").HasConversion<int>().IsRequired();

        builder.Property(x => x.AnalyzerProvider).HasColumnName("analyzer_provider").HasConversion<int>().IsRequired();
        builder.Property(x => x.PromptType).HasColumnName("prompt_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(20).IsRequired();
        builder.Property(x => x.PromptHash).HasColumnName("prompt_hash").HasMaxLength(64).IsRequired();
        // Raw LLM response — observable artifact only, never treated as business contract.
        builder.Property(x => x.LlmRawResponse).HasColumnName("llm_raw_response").HasColumnType("jsonb").IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_extraction_results_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.MovementId).HasDatabaseName("ix_int_extraction_results_movement_id");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_extraction_results_tenant_id");
        builder.HasIndex(x => new { x.MovementId, x.CreatedAt }).HasDatabaseName("ix_int_extraction_results_movement_created");
    }
}
