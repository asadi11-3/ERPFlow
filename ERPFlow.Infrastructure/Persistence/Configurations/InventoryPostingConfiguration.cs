using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Receiving;
using ERPFlow.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class InventoryPostingConfiguration : IEntityTypeConfiguration<InventoryPosting>
{
    public void Configure(EntityTypeBuilder<InventoryPosting> builder)
    {
        builder.ToTable("InventoryPostings", table =>
        {
            table.UseSqlOutputClause(false);
            table.HasCheckConstraint("CK_InventoryPostings_Kind", "([Kind]=0 AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NULL AND [StockTransferItemId] IS NULL) OR ([Kind] IN (1,2,3) AND [ReservationId] IS NOT NULL AND [GoodsReceiptItemId] IS NULL AND [StockTransferItemId] IS NULL) OR ([Kind]=4 AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NOT NULL AND [StockTransferItemId] IS NULL) OR ([Kind] IN (5,6) AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NULL AND [StockTransferItemId] IS NOT NULL)");
            table.HasCheckConstraint("CK_InventoryPostings_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.Id, x.Kind });
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(x => x.RequestHash).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => new { x.ActorId, x.IdempotencyKey }).IsUnique();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryReservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptItem>().WithMany().HasForeignKey(x => x.GoodsReceiptItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.GoodsReceiptItemId).IsUnique().HasFilter("[GoodsReceiptItemId] IS NOT NULL");
        builder.HasOne<StockTransferItem>().WithMany().HasForeignKey(x => x.StockTransferItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StockTransferItemId, x.Kind }).IsUnique().HasFilter("[StockTransferItemId] IS NOT NULL");
    }
}
