using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("GoodsReceipts", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_GoodsReceipts_Status", "([Status]=0 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=1 AND [PostedByUserId] IS NOT NULL AND [PostedAt] IS NOT NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=2 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NOT NULL AND [CancelledAt] IS NOT NULL AND LEN(LTRIM(RTRIM([CancellationReason])))>0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(48).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.SupplierDeliveryReference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.CancellationReason).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasAlternateKey(x => new { x.Id, x.PurchaseOrderId });
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => new { x.GoodsReceiptId, x.PurchaseOrderId })
            .HasPrincipalKey(x => new { x.Id, x.PurchaseOrderId }).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.SupplierId, x.SupplierDeliveryReference }).IsUnique().HasFilter("[SupplierDeliveryReference] IS NOT NULL AND [Status] <> 2");
        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => new { x.PurchaseOrderId, x.Status });
    }
}

public sealed class GoodsReceiptItemConfiguration : IEntityTypeConfiguration<GoodsReceiptItem>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptItem> b)
    {
        b.ToTable("GoodsReceiptItems", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_GoodsReceiptItems_Quantity", "[Quantity]>0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.GoodsReceiptId, x.PurchaseOrderItemId }).IsUnique();
        b.HasIndex(x => new { x.GoodsReceiptId, x.ProductId }).IsUnique();
        b.HasOne<PurchaseOrderItem>().WithMany().HasForeignKey(x => new { x.PurchaseOrderItemId, x.PurchaseOrderId, x.ProductId })
            .HasPrincipalKey(x => new { x.Id, x.PurchaseOrderId, x.ProductId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GoodsReceiptCommandConfiguration : IEntityTypeConfiguration<GoodsReceiptCommand>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptCommand> b)
    {
        b.ToTable("GoodsReceiptCommands", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_GoodsReceiptCommands_Operation", "[Operation] IN (0,1)");
            t.HasCheckConstraint("CK_GoodsReceiptCommands_Response", "ISJSON([ResponseJson])=1");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.RequestHash).HasColumnType("binary(32)").IsRequired();
        b.Property(x => x.ResponseJson).IsRequired();
        b.HasIndex(x => new { x.ActorId, x.IdempotencyKey }).IsUnique();
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<GoodsReceipt>().WithMany().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
}
