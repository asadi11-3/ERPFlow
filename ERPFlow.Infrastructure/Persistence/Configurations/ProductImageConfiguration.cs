using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages", table =>
        {
            table.HasCheckConstraint("CK_ProductImages_FileSize", "[FileSize] > 0 AND [FileSize] <= 5242880");
            table.HasCheckConstraint("CK_ProductImages_Dimensions", "[Width] > 0 AND [Height] > 0 AND [Width] <= 8192 AND [Height] <= 8192 AND CAST([Width] AS bigint) * [Height] <= 16000000");
            table.HasCheckConstraint("CK_ProductImages_ContentType", "[ContentType] IN ('image/jpeg', 'image/png', 'image/webp')");
        });
        builder.HasKey(image => image.Id);
        builder.Property(image => image.Id).ValueGeneratedNever();
        builder.HasIndex(image => image.ProductId).IsUnique();
        builder.HasOne<Product>().WithOne().HasForeignKey<ProductImage>(image => image.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(image => image.StorageKey).HasMaxLength(32).IsUnicode(false);
        builder.HasIndex(image => image.StorageKey).IsUnique();
        builder.HasOne<ProductImageObject>().WithOne().HasForeignKey<ProductImage>(image => image.StorageKey).OnDelete(DeleteBehavior.Restrict);
        builder.Property(image => image.ContentType).HasMaxLength(32).IsUnicode(false);
        builder.Property(image => image.RowVersion).IsRowVersion();
    }
}

public sealed class ProductImageObjectConfiguration : IEntityTypeConfiguration<ProductImageObject>
{
    public void Configure(EntityTypeBuilder<ProductImageObject> builder)
    {
        builder.ToTable("ProductImageObjects", table => table.HasCheckConstraint("CK_ProductImageObjects_State", "[State] IN (0, 1, 2)"));
        builder.HasKey(image => image.StorageKey);
        builder.Property(image => image.StorageKey).HasMaxLength(32).IsUnicode(false);
        builder.HasIndex(image => new { image.State, image.ExpiresAt });
        builder.HasOne<Product>().WithMany().HasForeignKey(image => image.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductImageStoreConfiguration : IEntityTypeConfiguration<ProductImageStore>
{
    public void Configure(EntityTypeBuilder<ProductImageStore> builder)
    {
        builder.ToTable("ProductImageStores", table => table.HasCheckConstraint("CK_ProductImageStores_Singleton", "[Id] = 1"));
        builder.HasKey(store => store.Id);
        builder.Property(store => store.Id).ValueGeneratedNever();
        builder.Property(store => store.Identity).HasMaxLength(64).IsUnicode(false);
    }
}
