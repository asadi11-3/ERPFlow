using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("StockTransfers", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_StockTransfers_Warehouses", "[SourceWarehouseId]<>[DestinationWarehouseId]");
            t.HasCheckConstraint("CK_StockTransfers_Status", "([Status]=0 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=1 AND [PostedByUserId] IS NOT NULL AND [PostedAt] IS NOT NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=2 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NOT NULL AND [CancelledAt] IS NOT NULL AND LEN(LTRIM(RTRIM([CancellationReason])))>0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(48).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.CancellationReason).HasMaxLength(1000);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.SourceWarehouseId, x.Status });
        b.HasIndex(x => new { x.DestinationWarehouseId, x.Status });
        b.HasIndex(x => new { x.CreatedAt, x.Id });
    }
}

public sealed class StockTransferItemConfiguration : IEntityTypeConfiguration<StockTransferItem>
{
    public void Configure(EntityTypeBuilder<StockTransferItem> b)
    {
        b.ToTable("StockTransferItems", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_StockTransferItems_Quantity", "[Quantity]>0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StockTransferId, x.ProductId }).IsUnique();
    }
}

public sealed class StockTransferCommandConfiguration : IEntityTypeConfiguration<StockTransferCommand>
{
    public void Configure(EntityTypeBuilder<StockTransferCommand> b)
    {
        b.ToTable("StockTransferCommands", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_StockTransferCommands_Operation", "[Operation] IN (0,1)");
            t.HasCheckConstraint("CK_StockTransferCommands_Response", "ISJSON([ResponseJson])=1");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.RequestHash).HasColumnType("binary(32)").IsRequired();
        b.Property(x => x.ResponseJson).IsRequired();
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockTransfer>().WithMany().HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ActorId, x.IdempotencyKey }).IsUnique();
    }
}
