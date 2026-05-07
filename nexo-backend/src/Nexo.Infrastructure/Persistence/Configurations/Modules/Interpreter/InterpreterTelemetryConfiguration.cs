using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class InterpreterTelemetryConfiguration : IEntityTypeConfiguration<InterpreterTelemetry>
{
    public void Configure(EntityTypeBuilder<InterpreterTelemetry> b)
    {
        b.ToTable("interpreter_telemetry", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.OperationType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.PromptType).HasMaxLength(100).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(20).IsRequired();
        b.Property(x => x.PromptHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);
        b.Property(x => x.FallbackFromProvider).HasMaxLength(50);
        b.Property(x => x.AnalyzerChainJson).HasMaxLength(500).IsRequired();
        b.Property(x => x.RawPrompt).HasMaxLength(8000);
        b.Property(x => x.RawResponse).HasMaxLength(8000);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}
