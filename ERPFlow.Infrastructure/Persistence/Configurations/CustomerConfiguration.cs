using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();

        builder.Property(customer => customer.Name)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(customer => customer.Email)
            .HasMaxLength(320)
            .IsRequired();
        builder.Property(customer => customer.IsActive)
            .HasDefaultValue(true)
            .IsRequired();
        builder.Property(customer => customer.CreatedAt)
            .HasPrecision(7)
            .IsRequired();

        builder.HasIndex(customer => customer.Email).IsUnique();
    }
}
