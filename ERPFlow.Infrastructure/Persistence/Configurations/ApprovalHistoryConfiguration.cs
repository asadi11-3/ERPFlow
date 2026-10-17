using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class ApprovalHistoryConfiguration : IEntityTypeConfiguration<ApprovalHistory>
{
    public void Configure(EntityTypeBuilder<ApprovalHistory> builder)
    {
        builder.ToTable("ApprovalHistories");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Id).ValueGeneratedNever();

        builder.Property(history => history.Action).IsRequired();
        builder.Property(history => history.Comments).HasMaxLength(1000);
        builder.Property(history => history.CreatedAt)
            .HasPrecision(7)
            .IsRequired();

        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(history => history.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(history => history.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(history => new { history.SalesOrderId, history.CreatedAt });
        builder.HasIndex(history => history.UserId);
    }
}
