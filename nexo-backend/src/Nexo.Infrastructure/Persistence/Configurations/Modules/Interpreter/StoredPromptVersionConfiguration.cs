using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class StoredPromptVersionConfiguration : IEntityTypeConfiguration<StoredPromptVersion>
{
    public void Configure(EntityTypeBuilder<StoredPromptVersion> b)
    {
        b.ToTable("stored_prompt_versions", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.PromptType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Version).HasMaxLength(20).IsRequired();
        b.Property(x => x.Hash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Content).HasMaxLength(16000).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();

        b.HasIndex(x => new { x.PromptType, x.IsActive });
        b.HasIndex(x => new { x.PromptType, x.Version }).IsUnique();
    }
}
