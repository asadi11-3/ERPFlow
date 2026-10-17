using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations", table =>
            table.HasCheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0"));
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();

        builder.Property(reservation => reservation.Quantity).IsRequired();
        builder.Property(reservation => reservation.IsReleased).IsRequired();
        builder.Property(reservation => reservation.CreatedAt)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(reservation => reservation.ReleasedAt).HasPrecision(7);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(reservation => reservation.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reservation => new { reservation.SalesOrderId, reservation.ProductId })
            .IsUnique();
        builder.HasIndex(reservation => reservation.ProductId);
    }
}
