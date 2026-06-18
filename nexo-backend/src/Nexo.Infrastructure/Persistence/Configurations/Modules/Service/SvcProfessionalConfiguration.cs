using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcProfessionalConfiguration : IEntityTypeConfiguration<SvcProfessional>
{
    public void Configure(EntityTypeBuilder<SvcProfessional> builder)
    {
        // Keys, tenant/store columns + FKs, is_active, audit columns, indexes.
        builder.ConfigureStoreScopedSvcEntity("svc_professionals");

        // Entity-specific columns
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(100);
        builder.Property(x => x.Specialty).HasColumnName("specialty").HasMaxLength(150);
        builder.Property(x => x.Color).HasColumnName("color").HasMaxLength(20);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(x => x.DefaultCommissionPercent)
            .HasColumnName("default_commission_percent").HasColumnType("numeric(5,2)");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.WorkingHoursJson)
            .HasColumnName("working_hours_json").HasColumnType("jsonb");
    }
}
