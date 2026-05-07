using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class AiProviderConfiguration : IEntityTypeConfiguration<AiProvider>
{
    public void Configure(EntityTypeBuilder<AiProvider> b)
    {
        b.ToTable("ai_providers", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.ModelId).HasMaxLength(100);
        b.Property(x => x.ApiKeyEncrypted).HasMaxLength(2000);
        b.Property(x => x.ApiKeyLastFour).HasMaxLength(10);

        b.HasIndex(x => x.Provider).IsUnique();
        b.HasIndex(x => x.Priority);
    }
}
