using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("SalesOrders", table => table.UseSqlOutputClause(false));
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        builder.Property(order => order.Status).IsRequired();
        builder.Property(order => order.CreatedAt)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(order => order.SubmittedAt).HasPrecision(7);
        builder.Property(order => order.DecidedAt).HasPrecision(7);
        builder.Property(order => order.RowVersion).IsRowVersion();

        builder.Ignore(order => order.TotalAmount);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(order => order.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(order => order.ReservationHistory);
        builder.HasMany(order => order.Reservations)
            .WithOne()
            .HasForeignKey(reservation => reservation.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.Reservations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(order => new { order.CustomerId, order.CreatedAt });
        builder.HasIndex(order => new { order.CreatedByUserId, order.CreatedAt });
        builder.HasIndex(order => order.Status);
    }
}
