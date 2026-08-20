using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Persistence.Configurations
{
    public class SessionExtensionConfiguration : IEntityTypeConfiguration<SessionExtension>
    {
        public void Configure(EntityTypeBuilder<SessionExtension> builder)
        {
            builder.ToTable("session_extensions");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(e => e.SessionId)
                .HasColumnName("session_id")
                .IsRequired();

            builder.Property(e => e.ExtendedDuration)
                .HasColumnName("extended_duration")
                .IsRequired();

            builder.Property(e => e.Cost)
                .HasColumnName("cost")
                .HasPrecision(18, 4)
                .IsRequired();

            builder.Property(e => e.Currency)
                .HasColumnName("currency")
                .HasMaxLength(10)
                .IsRequired();

            builder.Property(e => e.IdempotencyKey)
                .HasColumnName("idempotency_key")
                .HasMaxLength(128)
                .IsRequired();

            builder.Property(e => e.FinancialTransactionId)
                .HasColumnName("financial_transaction_id");

            builder.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            builder.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at");

            builder.HasIndex(e => e.SessionId)
                .HasDatabaseName("IX_session_extensions_session_id");

            builder.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("IX_session_extensions_idempotency_key");

            builder.HasOne<Session>()
                .WithMany()
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_session_extensions_sessions_session_id");
        }
    }
}
