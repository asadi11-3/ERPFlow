using ERPFlow.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ERPFlowDbContext
{
    internal bool StockTransferWriteInProgress { get; set; }
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<StockTransferCommand> StockTransferCommands => Set<StockTransferCommand>();

    private void ValidateTransferWrites()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity is not (StockTransfer or StockTransferItem or StockTransferCommand)) continue;
            if (!StockTransferWriteInProgress) throw new InvalidOperationException("Transfer changes require the document workflow boundary.");
            if (entry.State == EntityState.Deleted || (entry.Entity is StockTransferItem or StockTransferCommand && entry.State != EntityState.Added))
                throw new InvalidOperationException("Transfer lines and command results are immutable and cannot be deleted.");
            if (entry.Entity is StockTransfer && entry.State == EntityState.Modified &&
                (entry.Property(nameof(StockTransfer.Status)).OriginalValue is not StockTransferStatus.Draft ||
                    entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(StockTransfer.Status) or nameof(StockTransfer.PostedByUserId) or
                        nameof(StockTransfer.PostedAt) or nameof(StockTransfer.CancelledByUserId) or nameof(StockTransfer.CancelledAt) or nameof(StockTransfer.CancellationReason)))))
                throw new InvalidOperationException("Only Draft posting/cancellation may change a transfer.");
        }
    }
}
