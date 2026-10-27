using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class InventoryAuthorityConfiguration : IEntityTypeConfiguration<InventoryAuthority>
{
    public void Configure(EntityTypeBuilder<InventoryAuthority> builder)
    {
        builder.ToTable("InventoryAuthorities", table =>
        {
            table.UseSqlOutputClause(false);
            table.HasCheckConstraint("CK_InventoryAuthorities_Singleton", "[Id] = 1");
            table.HasCheckConstraint("CK_InventoryAuthorities_State", "([Mode] = 0 AND [MinimumWriterVersion] = 1 AND [PreparationId] IS NULL AND [CompletedAt] IS NULL) OR ([Mode] = 1 AND [MinimumWriterVersion] = 2 AND [PreparationId] IS NOT NULL AND [CompletedAt] IS NOT NULL AND [ApprovedByUserId] IS NOT NULL AND [DefaultWarehouseId] IS NOT NULL AND [PhysicalAttributionApproval] IS NOT NULL AND [SourceFingerprint] IS NOT NULL AND [CommandHash] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.PhysicalAttributionApproval).HasMaxLength(200);
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)");
        builder.Property(x => x.CommandHash).HasColumnType("binary(32)");
        builder.HasOne<LegacyInventoryImportBatch>().WithMany().HasForeignKey(x => x.PreparationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.DefaultWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasData(new { Id = 1, Mode = InventoryAuthorityMode.Product, MinimumWriterVersion = 1,
            ImportedStocks = 0, ImportedReservations = 0 });
    }
}
