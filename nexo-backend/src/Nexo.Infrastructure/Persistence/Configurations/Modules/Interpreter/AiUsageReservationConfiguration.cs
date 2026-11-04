using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class AiUsageReservationConfiguration : IEntityTypeConfiguration<AiUsageReservation>
{
    public void Configure(EntityTypeBuilder<AiUsageReservation> b)
    {
        b.ToTable("ai_usage_reservations", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.ModelId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.HasIndex(x => new { x.TenantId, x.MonthStart, x.Status }).HasDatabaseName("ix_ai_usage_tenant_month");
        b.HasIndex(x => new { x.Provider, x.MonthStart, x.Status }).HasDatabaseName("ix_ai_usage_provider_month");
    }
}
