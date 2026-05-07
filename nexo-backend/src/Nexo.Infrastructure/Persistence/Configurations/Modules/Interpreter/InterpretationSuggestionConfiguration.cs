using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class InterpretationSuggestionConfiguration : IEntityTypeConfiguration<InterpretationSuggestion>
{
    public void Configure(EntityTypeBuilder<InterpretationSuggestion> builder)
    {
        builder.ToTable("int_interpretation_suggestions", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.MovementId).HasColumnName("movement_id").IsRequired();

        builder.Property(x => x.SuggestedDirection).HasColumnName("suggested_direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.DirectionSource).HasColumnName("direction_source").HasConversion<int>().IsRequired();

        builder.Property(x => x.SuggestedNature).HasColumnName("suggested_nature").HasConversion<int>().IsRequired();
        builder.Property(x => x.NatureSource).HasColumnName("nature_source").HasConversion<int>().IsRequired();

        builder.Property(x => x.SuggestedCategoryId).HasColumnName("suggested_category_id");
        builder.Property(x => x.CategorySource).HasColumnName("category_source").HasConversion<int>().IsRequired();

        builder.Property(x => x.SuggestedContextType).HasColumnName("suggested_context_type").HasConversion<int>();
        builder.Property(x => x.SuggestedContextId).HasColumnName("suggested_context_id");
        builder.Property(x => x.ContextSource).HasColumnName("context_source").HasConversion<int>().IsRequired();

        builder.Property(x => x.SuggestedAccountId).HasColumnName("suggested_account_id");
        builder.Property(x => x.AccountSource).HasColumnName("account_source").HasConversion<int>().IsRequired();

        builder.Property(x => x.WasAccepted).HasColumnName("was_accepted");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_suggestions_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.MovementId).HasDatabaseName("ix_int_suggestions_movement_id");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_suggestions_tenant_id");
        builder.HasIndex(x => new { x.MovementId, x.CreatedAt }).HasDatabaseName("ix_int_suggestions_movement_created");
    }
}
