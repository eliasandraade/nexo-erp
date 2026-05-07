using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class ReprocessLogConfiguration : IEntityTypeConfiguration<ReprocessLog>
{
    public void Configure(EntityTypeBuilder<ReprocessLog> builder)
    {
        builder.ToTable("int_reprocess_logs", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.MovementId).HasColumnName("movement_id").IsRequired();
        builder.Property(x => x.TriggeredBy).HasColumnName("triggered_by").IsRequired();
        builder.Property(x => x.TriggerReason).HasColumnName("trigger_reason").HasConversion<int>().IsRequired();
        builder.Property(x => x.PreviousExtractionResultId).HasColumnName("previous_extraction_result_id").IsRequired();
        builder.Property(x => x.NewExtractionResultId).HasColumnName("new_extraction_result_id").IsRequired();
        builder.Property(x => x.PreviousSuggestionId).HasColumnName("previous_suggestion_id").IsRequired();
        builder.Property(x => x.NewSuggestionId).HasColumnName("new_suggestion_id").IsRequired();
        builder.Property(x => x.AnalyzerProvider).HasColumnName("analyzer_provider").HasConversion<int>().IsRequired();
        builder.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(20).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamptz");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");
        builder.Property(x => x.WasAccepted).HasColumnName("was_accepted");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        // Structured diff of before/after extraction and suggestion — JSONB for observability and tuning.
        builder.Property(x => x.DiffJson).HasColumnName("diff_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_reprocess_logs_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.MovementId).HasDatabaseName("ix_int_reprocess_logs_movement_id");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_reprocess_logs_tenant_id");
        builder.HasIndex(x => new { x.TenantId, x.StartedAt }).HasDatabaseName("ix_int_reprocess_logs_tenant_started");
    }
}
