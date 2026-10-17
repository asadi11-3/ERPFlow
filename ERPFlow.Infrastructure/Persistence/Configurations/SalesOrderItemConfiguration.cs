using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class SalesOrderItemConfiguration : IEntityTypeConfiguration<SalesOrderItem>
{
    public void Configure(EntityTypeBuilder<SalesOrderItem> builder)
    {
        builder.ToTable("SalesOrderItems", table =>
        {
            table.HasCheckConstraint("CK_SalesOrderItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_SalesOrderItems_UnitPrice", "[UnitPrice] >= 0");
        });
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Ignore(item => item.LineTotal);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.SalesOrderId, item.ProductId }).IsUnique();
    }
}
