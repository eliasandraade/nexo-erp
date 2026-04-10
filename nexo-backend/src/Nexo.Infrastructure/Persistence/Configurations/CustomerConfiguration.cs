using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.PersonType)
            .HasColumnName("person_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TradeName)
            .HasColumnName("trade_name")
            .HasMaxLength(200);

        builder.Property(x => x.DocumentType)
            .HasColumnName("document_type")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.DocumentNumber)
            .HasColumnName("document_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(200);

        builder.Property(x => x.Phone)
            .HasColumnName("phone")
            .HasMaxLength(30);

        builder.Property(x => x.WhatsApp)
            .HasColumnName("whatsapp")
            .HasMaxLength(30);

        builder.Property(x => x.AddressJson)
            .HasColumnName("address_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.CreditLimit)
            .HasColumnName("credit_limit")
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.DocumentNumber }).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Name });

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
