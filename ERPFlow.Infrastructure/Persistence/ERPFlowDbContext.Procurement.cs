using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ERPFlowDbContext
{
    private void ValidateProcurementWrites()
    {
        if (ChangeTracker.Entries<PurchaseOrderHistory>().Any(x => x.State == EntityState.Added))
            throw new InvalidOperationException("PO audit additions require the atomic transition boundary.");
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity is PurchaseOrderHistory)
                throw new InvalidOperationException("PO history is append-only.");
            if (entry.Entity is Warehouse or Supplier or PurchaseOrder && entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Procurement masters and orders retain history; use deactivation or cancellation.");
            if (entry.Entity is Warehouse && entry.Property(nameof(Warehouse.Code)).IsModified)
                throw new InvalidOperationException("Warehouse codes are immutable.");
            if (entry.Entity is Supplier && entry.Property(nameof(Supplier.SupplierCode)).IsModified)
                throw new InvalidOperationException("Supplier codes are immutable.");
            if (entry.Entity is PurchaseOrder)
            {
                if (entry.Property(nameof(PurchaseOrder.PONumber)).IsModified || entry.Property(nameof(PurchaseOrder.CreatedByUserId)).IsModified ||
                    entry.Property(nameof(PurchaseOrder.CreatedAt)).IsModified || entry.Property(nameof(PurchaseOrder.ReceivingStartedAt)).IsModified)
                    throw new InvalidOperationException("PO identity, ownership and receiving admission cannot be changed in Phase 3.");
                if (entry.Property(nameof(PurchaseOrder.Status)).OriginalValue is PurchaseOrderStatus status && status != PurchaseOrderStatus.Draft &&
                    new[] { nameof(PurchaseOrder.SupplierId), nameof(PurchaseOrder.WarehouseId), nameof(PurchaseOrder.TotalAmount), nameof(PurchaseOrder.Notes) }
                        .Any(x => entry.Property(x).IsModified))
                    throw new InvalidOperationException("Submitted commercial terms are immutable.");
            }
        }
        foreach (var entry in ChangeTracker.Entries<PurchaseOrderItem>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var parent = ChangeTracker.Entries<PurchaseOrder>().SingleOrDefault(x => x.Entity.Id == entry.Entity.PurchaseOrderId);
            if (parent is null || (parent.State != EntityState.Added &&
                (parent.Property(x => x.Status).OriginalValue != PurchaseOrderStatus.Draft ||
                 parent.State != EntityState.Modified || parent.Entity.Revision <= parent.Property(x => x.Revision).OriginalValue)))
                throw new InvalidOperationException("PO lines require a Draft parent revision/concurrency update in the same transaction.");
        }
    }
}
