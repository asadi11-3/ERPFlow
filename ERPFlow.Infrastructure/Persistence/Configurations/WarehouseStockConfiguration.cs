using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class WarehouseStockConfiguration : IEntityTypeConfiguration<WarehouseStock>
{
    public void Configure(EntityTypeBuilder<WarehouseStock> builder)
    {
        builder.ToTable("WarehouseStocks", table =>
        {
            table.HasCheckConstraint("CK_WarehouseStocks_Balances", "[StockOnHand] >= 0 AND [ReservedQuantity] >= 0 AND [ReservedQuantity] <= [StockOnHand]");
            table.HasCheckConstraint("CK_WarehouseStocks_Sequence", "[Sequence] >= 0 AND ([Sequence] > 0 OR ([StockOnHand] = 0 AND [ReservedQuantity] = 0))");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.AvailableStock);
        builder.HasAlternateKey(x => new { x.Id, x.ProductId });
        builder.HasIndex(x => new { x.WarehouseId, x.ProductId }).IsUnique();
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
