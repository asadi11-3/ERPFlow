using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("InventoryReservations", table =>
        {
            table.HasCheckConstraint("CK_InventoryReservations_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_InventoryReservations_Owner", "([SalesOrderId] IS NOT NULL AND [JobcardItemId] IS NULL) OR ([SalesOrderId] IS NULL AND [JobcardItemId] IS NOT NULL)");
            table.HasCheckConstraint("CK_InventoryReservations_Status", "([Status] = 0 AND [ClosedAt] IS NULL) OR ([Status] IN (1, 2) AND ([ClosedAt] IS NOT NULL OR [IsLegacyImport] = 1)) OR ([Status] = 3 AND [IsLegacyImport] = 1)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<WarehouseStock>().WithMany().HasForeignKey(x => new { x.WarehouseStockId, x.ProductId })
            .HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobcardItem>().WithMany().HasForeignKey(x => x.JobcardItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SalesOrderId, x.ProductId }).IsUnique()
            .HasFilter("[SalesOrderId] IS NOT NULL AND [Status] = 0");
        builder.HasIndex(x => x.JobcardItemId).IsUnique().HasFilter("[JobcardItemId] IS NOT NULL");
        builder.HasIndex(x => new { x.WarehouseStockId, x.Status });
    }
}
