using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses", table =>
        {
            table.HasCheckConstraint("CK_Warehouses_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
            table.HasCheckConstraint("CK_Warehouses_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasData(new
        {
            Id = LegacyDefaultWarehouse.Id,
            Code = LegacyDefaultWarehouse.Code,
            Name = "Legacy inventory compatibility warehouse",
            IsActive = true,
            CreatedAt = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
