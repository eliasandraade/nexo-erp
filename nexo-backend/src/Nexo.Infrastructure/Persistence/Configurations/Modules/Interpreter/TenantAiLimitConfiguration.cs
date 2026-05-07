using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class TenantAiLimitConfiguration : IEntityTypeConfiguration<TenantAiLimit>
{
    public void Configure(EntityTypeBuilder<TenantAiLimit> b)
    {
        b.ToTable("tenant_ai_limits", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.TenantId).IsUnique();
    }
}
