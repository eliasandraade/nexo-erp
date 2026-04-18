using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.RefreshJti).IsRequired().HasMaxLength(100);
        builder.Property(s => s.IpAddress).HasMaxLength(50);
        builder.Property(s => s.UserAgent).HasMaxLength(500);
        builder.Property(s => s.LastUsedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.ExpiresAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.IsRevoked).IsRequired().HasDefaultValue(false);

        // Indexes — most platform queries are "all sessions for userId"
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.RefreshJti).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.IsRevoked });

        // Cascade delete when tenant is deleted
        builder.HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
