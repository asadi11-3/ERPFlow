using ERPFlow.Domain.Receiving;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ERPFlowDbContext
{
    internal bool GoodsReceiptWriteInProgress { get; set; }
    private void ValidateReceivingWrites()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity is not (GoodsReceipt or GoodsReceiptItem or GoodsReceiptCommand)) continue;
            if (!GoodsReceiptWriteInProgress) throw new InvalidOperationException("GRN writes require the receiving workflow boundary.");
            if (entry.State == EntityState.Deleted || (entry.Entity is GoodsReceiptItem or GoodsReceiptCommand && entry.State != EntityState.Added))
                throw new InvalidOperationException("Receipt lines and command history are immutable and cannot be deleted.");
            if (entry.Entity is GoodsReceipt && entry.State == EntityState.Modified &&
                (entry.Property(nameof(GoodsReceipt.Status)).OriginalValue is not GoodsReceiptStatus.Draft ||
                 entry.Property(nameof(GoodsReceipt.Status)).CurrentValue is not GoodsReceiptStatus.Cancelled ||
                 entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(GoodsReceipt.Status) or nameof(GoodsReceipt.CancelledByUserId) or
                     nameof(GoodsReceipt.CancelledAt) or nameof(GoodsReceipt.CancellationReason)))))
                throw new InvalidOperationException("Only Draft cancellation is an ordinary EF receipt mutation; posting requires atomic SQL finalization.");
        }
    }
}
