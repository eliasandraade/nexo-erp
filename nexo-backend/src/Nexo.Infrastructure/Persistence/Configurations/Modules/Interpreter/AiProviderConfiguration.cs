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
        b.Property(x => x.LastTestStatus).HasMaxLength(40);
        b.Property(x => x.LastTestMessage).HasMaxLength(300);

        b.HasIndex(x => x.Provider).IsUnique();
        // At most one default provider, whatever the concurrency of the SuperAdmin's edits.
        b.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\" = true").HasDatabaseName("ux_ai_providers_single_default");
        b.HasIndex(x => x.Priority);
    }
}
