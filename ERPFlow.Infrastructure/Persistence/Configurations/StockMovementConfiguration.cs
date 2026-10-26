using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", table =>
        {
            table.UseSqlOutputClause(false);
            table.HasCheckConstraint("CK_StockMovements_Balances", "[BeforeOnHand] >= [BeforeReserved] AND [BeforeReserved] >= 0 AND [AfterOnHand] >= [AfterReserved] AND [AfterReserved] >= 0");
            table.HasCheckConstraint("CK_StockMovements_Deltas", "[AfterOnHand] - [BeforeOnHand] = [PhysicalDelta] AND [AfterReserved] - [BeforeReserved] = [ReservedDelta]");
            table.HasCheckConstraint("CK_StockMovements_Sequence", "[Sequence] > 0");
            table.HasCheckConstraint("CK_StockMovements_Kind", "([Kind] = 0 AND [Sequence] = 1 AND [BeforeOnHand] = 0 AND [BeforeReserved] = 0 AND [PhysicalDelta] >= 0 AND [ReservedDelta] >= 0) OR ([Kind] = 1 AND [PhysicalDelta] = 0 AND [ReservedDelta] > 0) OR ([Kind] = 2 AND [PhysicalDelta] = 0 AND [ReservedDelta] < 0) OR ([Kind] = 3 AND [PhysicalDelta] = [ReservedDelta] AND [PhysicalDelta] < 0) OR ([Kind]=4 AND [PhysicalDelta]>0 AND [ReservedDelta]=0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ResultRowVersion).HasColumnType("binary(8)").IsRequired();
        builder.HasOne<InventoryPosting>().WithMany().HasForeignKey(x => new { x.PostingId, x.Kind })
            .HasPrincipalKey(x => new { x.Id, x.Kind }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WarehouseStock>().WithMany().HasForeignKey(x => x.WarehouseStockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PostingId, x.WarehouseStockId }).IsUnique();
        builder.HasIndex(x => new { x.WarehouseStockId, x.Sequence }).IsUnique();
        builder.HasIndex(x => x.WarehouseStockId).IsUnique().HasFilter("[Kind] = 0")
            .HasDatabaseName("IX_StockMovements_Opening");
    }
}
