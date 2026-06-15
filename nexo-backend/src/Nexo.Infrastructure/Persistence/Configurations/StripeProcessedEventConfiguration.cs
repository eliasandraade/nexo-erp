using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class StripeProcessedEventConfiguration : IEntityTypeConfiguration<StripeProcessedEvent>
{
    public void Configure(EntityTypeBuilder<StripeProcessedEvent> builder)
    {
        builder.ToTable("stripe_processed_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.StripeEventId)
            .HasColumnName("stripe_event_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Each Stripe event ID must be unique — this is the idempotency key.
        builder.HasIndex(x => x.StripeEventId).IsUnique();
    }
}
