using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Name)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(user => user.Email)
            .HasMaxLength(320)
            .IsRequired();
        builder.Property(user => user.Role).IsRequired();
        builder.Property(user => user.CreatedAt)
            .HasPrecision(7)
            .IsRequired();

        builder.HasIndex(user => user.Email).IsUnique();
    }
}
