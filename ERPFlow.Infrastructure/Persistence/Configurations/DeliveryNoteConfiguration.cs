using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class DeliveryNoteConfiguration : IEntityTypeConfiguration<DeliveryNote>
{
    public void Configure(EntityTypeBuilder<DeliveryNote> builder)
    {
        builder.ToTable("DeliveryNotes");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.DeliveryNumber)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(d => d.Carrier)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(d => d.TrackingNumber)
            .HasMaxLength(100);
        builder.Property(d => d.DispatchedAt)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(d => d.DeliveredAt)
            .HasPrecision(7);

        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(d => d.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.DeliveryNumber).IsUnique();
        builder.HasIndex(d => d.SalesOrderId);
        builder.HasIndex(d => d.CustomerId);
    }
}
