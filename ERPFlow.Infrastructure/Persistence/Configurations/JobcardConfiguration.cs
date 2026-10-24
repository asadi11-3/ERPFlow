using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class JobcardConfiguration : IEntityTypeConfiguration<Jobcard>
{
    public void Configure(EntityTypeBuilder<Jobcard> builder)
    {
        builder.ToTable("Jobcards");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.Property(j => j.TicketNumber)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(j => j.TechnicianName)
            .HasMaxLength(150)
            .IsRequired();
        builder.Property(j => j.ReportedFault)
            .HasMaxLength(1000)
            .IsRequired();
        builder.Property(j => j.Priority).IsRequired();
        builder.Property(j => j.Status).IsRequired();
        builder.Property(j => j.Revision)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(j => j.CreatedAt)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(j => j.CompletedAt)
            .HasPrecision(7);
        builder.Property(j => j.TechnicianNotes)
            .HasMaxLength(2000);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(j => j.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MachineAsset>()
            .WithMany()
            .HasForeignKey(j => j.MachineAssetId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(j => j.Items)
            .WithOne()
            .HasForeignKey(item => item.JobcardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(j => j.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(j => j.TicketNumber).IsUnique();
        builder.HasIndex(j => j.CustomerId);
        builder.HasIndex(j => j.Status);
    }
}
