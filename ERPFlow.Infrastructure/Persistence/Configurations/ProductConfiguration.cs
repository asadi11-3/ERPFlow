using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_UnitPrice", "[UnitPrice] >= 0");
            table.HasCheckConstraint("CK_Products_StockOnHand", "[StockOnHand] >= 0");
            table.HasCheckConstraint("CK_Products_ReservedQuantity", "[ReservedQuantity] >= 0 AND [ReservedQuantity] <= [StockOnHand]");
        });
        builder.HasKey(product => product.Id);

        builder.Property(product => product.Name)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(product => product.Sku)
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(product => product.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(product => product.StockOnHand).IsRequired();
        builder.Property(product => product.ReservedQuantity).IsRequired();
        builder.Property(product => product.CreatedAt)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(product => product.RowVersion).IsRowVersion();

        builder.Ignore(product => product.AvailableStock);
        builder.HasIndex(product => product.Sku).IsUnique();
    }
}
