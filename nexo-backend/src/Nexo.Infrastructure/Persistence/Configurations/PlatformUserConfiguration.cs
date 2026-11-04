using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("platform_users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Role)
            .HasColumnName("role")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64).IsRequired();
        builder.Property(x => x.MfaEnabled).HasColumnName("mfa_enabled").IsRequired();
        builder.Property(x => x.MfaSecret).HasColumnName("mfa_secret").HasMaxLength(500);
        builder.Property(x => x.MfaPendingSecret).HasColumnName("mfa_pending_secret").HasMaxLength(500);
        builder.Property(x => x.MfaLastUsedStep).HasColumnName("mfa_last_used_step");
        builder.Property(x => x.MfaRecoveryCodes).HasColumnName("mfa_recovery_codes").HasMaxLength(2000);

        builder.HasIndex(x => x.Email).IsUnique();
    }
}

public class PlatformSessionConfiguration : IEntityTypeConfiguration<PlatformSession>
{
    public void Configure(EntityTypeBuilder<PlatformSession> builder)
    {
        builder.ToTable("platform_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.PlatformUserId).HasColumnName("platform_user_id").IsRequired();
        builder.Property(x => x.RefreshTokenHash).HasColumnName("refresh_token_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PreviousRefreshTokenHash).HasColumnName("previous_refresh_token_hash").HasMaxLength(64);
        builder.Property(x => x.RotatedAt).HasColumnName("rotated_at").HasColumnType("timestamptz");
        builder.Property(x => x.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64).IsRequired();
        builder.Property(x => x.MfaVerified).HasColumnName("mfa_verified").IsRequired();
        builder.Property(x => x.PendingMfa).HasColumnName("pending_mfa").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
        builder.Property(x => x.RevokedReason).HasColumnName("revoked_reason").HasMaxLength(60);
        builder.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasColumnName("user_agent").HasMaxLength(300);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.PlatformUserId)
            .HasConstraintName("fk_platform_sessions_user").OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.RefreshTokenHash).IsUnique().HasDatabaseName("ux_platform_sessions_refresh_hash");
        builder.HasIndex(x => x.PreviousRefreshTokenHash).HasDatabaseName("ix_platform_sessions_previous_hash");
        builder.HasIndex(x => new { x.PlatformUserId, x.ExpiresAt }).HasDatabaseName("ix_platform_sessions_user_expires");
    }
}
