using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class ProductCatalogChangeConfiguration : IEntityTypeConfiguration<ProductCatalogChange>
{
    public void Configure(EntityTypeBuilder<ProductCatalogChange> builder)
    {
        builder.ToTable("ProductCatalogChanges", table =>
        {
            table.HasCheckConstraint("CK_ProductCatalogChanges_Action", "[Action] IN ('Created','Updated','Activated','Deactivated')");
            table.HasCheckConstraint("CK_ProductCatalogChanges_Price", "[UnitPrice] >= 0 AND ([PreviousUnitPrice] IS NULL OR [PreviousUnitPrice] >= 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(16);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.PreviousName).HasMaxLength(200);
        builder.Property(x => x.Sku).HasMaxLength(64);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.PreviousUnitPrice).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.ProductId, x.CreatedAt });
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
