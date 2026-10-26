using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("Suppliers", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_Suppliers_Code", "LEN(LTRIM(RTRIM([SupplierCode]))) BETWEEN 1 AND 32");
            t.HasCheckConstraint("CK_Suppliers_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SupplierCode).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.SupplierCode).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContactName).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Address).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("PurchaseOrders", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_PurchaseOrders_Status", "[Status] IN (0,1,2,3,4,5,6,7)");
            t.HasCheckConstraint("CK_PurchaseOrders_Total", "[TotalAmount] >= 0");
            t.HasCheckConstraint("CK_PurchaseOrders_Revision", "[Revision] > 0");
            t.HasCheckConstraint("CK_PurchaseOrders_Number", "LEN(LTRIM(RTRIM([PONumber]))) > 0");
            t.HasCheckConstraint("CK_PurchaseOrders_Receiving", "[ReceivingStartedAt] IS NULL OR [Status] IN (2,5,6,7)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PONumber).HasMaxLength(48).IsRequired();
        b.HasIndex(x => x.PONumber).IsUnique();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.TotalAmount).HasPrecision(28, 4);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt });
    }
}

public sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> b)
    {
        b.ToTable("PurchaseOrderItems", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_PurchaseOrderItems_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_PurchaseOrderItems_Cost", "[UnitCost] BETWEEN 0 AND 999999999999.9999");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.UnitCost).HasPrecision(16, 4);
        b.Ignore(x => x.LineTotal);
        b.HasIndex(x => new { x.PurchaseOrderId, x.ProductId }).IsUnique();
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderHistoryConfiguration : IEntityTypeConfiguration<PurchaseOrderHistory>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderHistory> b)
    {
        b.ToTable("PurchaseOrderHistories", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_PurchaseOrderHistories_Action", "([Action]=0 AND [FromStatus]=0 AND [ToStatus]=1) OR ([Action]=1 AND [FromStatus]=1 AND [ToStatus]=2) OR ([Action]=2 AND [FromStatus]=1 AND [ToStatus]=3) OR ([Action]=3 AND [FromStatus] IN (0,1,2) AND [ToStatus]=4)");
            t.HasCheckConstraint("CK_PurchaseOrderHistories_Revision", "[Revision] > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Comments).HasMaxLength(1000);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PurchaseOrderId, x.Revision }).IsUnique();
    }
}
