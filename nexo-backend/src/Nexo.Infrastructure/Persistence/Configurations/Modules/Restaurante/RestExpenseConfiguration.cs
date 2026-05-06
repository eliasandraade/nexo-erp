using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestExpenseConfiguration : IEntityTypeConfiguration<RestExpense>
{
    public void Configure(EntityTypeBuilder<RestExpense> builder)
    {
        builder.ToTable("rest_expenses", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CompetenceDate).HasColumnName("competence_date").IsRequired();
        builder.Property(x => x.PaymentDate).HasColumnName("payment_date");
        builder.Property(x => x.IsRecurring).HasColumnName("is_recurring")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_expenses_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_expenses_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_rest_expenses_store_id");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.CompetenceDate })
            .HasDatabaseName("ix_rest_expenses_tenant_store_competence");
    }
}
