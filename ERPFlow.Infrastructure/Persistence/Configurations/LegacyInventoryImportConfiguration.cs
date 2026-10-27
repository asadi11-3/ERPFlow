using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class LegacyInventoryImportConfiguration :
    IEntityTypeConfiguration<LegacyInventoryImportBatch>,
    IEntityTypeConfiguration<LegacyInventoryOpening>,
    IEntityTypeConfiguration<LegacyInventoryReservationImport>
{
    public void Configure(EntityTypeBuilder<LegacyInventoryImportBatch> builder)
    {
        builder.ToTable("LegacyInventoryImportBatches", table =>
            table.HasCheckConstraint("CK_LegacyInventoryImportBatches_Format", "[FormatVersion] = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PreparedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.DefaultWarehouseId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<LegacyInventoryOpening> builder)
    {
        builder.ToTable("LegacyInventoryOpenings", table => table.HasCheckConstraint(
            "CK_LegacyInventoryOpenings_Balances", "[StockOnHand] >= [ReservedQuantity] AND [ReservedQuantity] >= 0"));
        builder.HasKey(x => new { x.BatchId, x.ProductId });
        builder.Property(x => x.SourceProductRowVersion).HasColumnType("binary(8)").IsRequired();
        builder.HasIndex(x => new { x.BatchId, x.TargetWarehouseStockId }).IsUnique();
        builder.HasOne<LegacyInventoryImportBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<LegacyInventoryReservationImport> builder)
    {
        builder.ToTable("LegacyInventoryReservationImports", table =>
        {
            table.HasCheckConstraint("CK_LegacyInventoryReservationImports_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_LegacyInventoryReservationImports_Owner",
                "([SourceKind] = 0 AND [SalesOrderId] IS NOT NULL AND [JobcardItemId] IS NULL AND [TargetReservationId] = [SourceId]) OR ([SourceKind] = 1 AND [SalesOrderId] IS NULL AND [JobcardItemId] IS NOT NULL AND [JobcardItemId] = [SourceId])");
            table.HasCheckConstraint("CK_LegacyInventoryReservationImports_Disposition", "[Disposition] IN (0,1,2,3) AND ([IsHistorical] = 0 OR [Disposition] <> 0)");
        });
        builder.HasKey(x => new { x.BatchId, x.SourceKind, x.SourceId });
        builder.HasIndex(x => new { x.BatchId, x.TargetReservationId }).IsUnique();
        builder.HasIndex(x => new { x.BatchId, x.ProductId, x.Disposition });
        builder.HasOne<LegacyInventoryImportBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
