using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ERPFlowDbContext
{
    internal bool InventoryPostingInProgress { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateInventoryWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateInventoryWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateInventoryWrites()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (entry.Entity is StockMovement or InventoryPosting && entry.State != EntityState.Added)
                throw new InvalidOperationException("Inventory posting history is append-only.");
            if (entry.Entity is WarehouseStock && entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Stock rows cannot be deleted or their history reset.");
            if (entry.Entity is InventoryReservation && entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Reservation history cannot be deleted.");
            if (entry.Entity is WarehouseStock && entry.State == EntityState.Modified &&
                (entry.Property(nameof(WarehouseStock.WarehouseId)).IsModified || entry.Property(nameof(WarehouseStock.ProductId)).IsModified))
                throw new InvalidOperationException("A stock row's warehouse and product are immutable.");
            if (InventoryPostingInProgress && entry.Entity is Product && entry.State == EntityState.Modified &&
                (entry.Property(nameof(Product.StockOnHand)).IsModified || entry.Property(nameof(Product.ReservedQuantity)).IsModified))
                throw new InvalidOperationException("Legacy Product balance writes cannot be mixed with warehouse posting.");
            if (InventoryPostingInProgress) continue;
            if (entry.Entity is StockMovement or InventoryPosting or InventoryReservation)
                throw new InvalidOperationException("Inventory writes require the approved posting boundary.");
            if (entry.Entity is WarehouseStock stock && (entry.State != EntityState.Added ||
                stock.StockOnHand != 0 || stock.ReservedQuantity != 0 || stock.Sequence != 0))
                throw new InvalidOperationException("Warehouse balances can only change through the posting boundary.");
        }
    }
}
