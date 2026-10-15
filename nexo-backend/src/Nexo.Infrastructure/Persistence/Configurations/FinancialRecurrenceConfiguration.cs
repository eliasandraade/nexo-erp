using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class FinancialRecurrenceConfiguration : IEntityTypeConfiguration<FinancialRecurrence>
{
    public void Configure(EntityTypeBuilder<FinancialRecurrence> builder)
    {
        builder.ToTable("financial_recurrences", t =>
        {
            t.HasCheckConstraint("ck_financial_recurrences_amount", "amount > 0");
            t.HasCheckConstraint("ck_financial_recurrences_day", "day_of_month BETWEEN 1 AND 31");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.TransactionType).HasColumnName("transaction_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.DayOfMonth).HasColumnName("day_of_month").IsRequired();
        builder.Property(x => x.StartMonth).HasColumnName("start_month").HasColumnType("date").IsRequired();
        builder.Property(x => x.EndMonth).HasColumnName("end_month").HasColumnType("date");
        builder.Property(x => x.GeneratedThrough).HasColumnName("generated_through").HasColumnType("date");
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.StoreId).HasColumnName("store_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.IsActive });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
