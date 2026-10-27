using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class JobcardItemConfiguration : IEntityTypeConfiguration<JobcardItem>
{
    public void Configure(EntityTypeBuilder<JobcardItem> builder)
    {
        builder.ToTable("JobcardItems", table => table.UseSqlOutputClause(false));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Ignore(item => item.LineTotal);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(item => item.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.JobcardId, item.ProductId }).IsUnique();
    }
}
