using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class MachineAssetConfiguration : IEntityTypeConfiguration<MachineAsset>
{
    public void Configure(EntityTypeBuilder<MachineAsset> builder)
    {
        builder.ToTable("MachineAssets");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.TagNumber)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(m => m.SerialNumber)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(m => m.Status)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(m => m.InstalledAt)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(m => m.WarrantyExpiresAt)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(m => m.MeterReading)
            .IsRequired();

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(m => m.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(m => m.SalesOrderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => m.TagNumber).IsUnique();
        builder.HasIndex(m => m.SerialNumber).IsUnique();
        builder.HasIndex(m => m.CustomerId);
    }
}
