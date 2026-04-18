using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class TenantNoteConfiguration : IEntityTypeConfiguration<TenantNote>
{
    public void Configure(EntityTypeBuilder<TenantNote> builder)
    {
        builder.ToTable("tenant_notes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.Content).IsRequired().HasMaxLength(4000);
        builder.Property(n => n.AuthorName).IsRequired().HasMaxLength(200);
        builder.Property(n => n.IsPinned).IsRequired();

        builder.HasIndex(n => n.TenantId);
        builder.HasIndex(n => new { n.TenantId, n.IsPinned });

        builder.HasOne(n => n.Tenant)
            .WithMany()
            .HasForeignKey(n => n.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
