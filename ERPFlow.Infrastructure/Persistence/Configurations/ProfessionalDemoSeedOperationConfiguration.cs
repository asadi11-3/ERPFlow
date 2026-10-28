using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalDemoSeedOperationConfiguration : IEntityTypeConfiguration<ProfessionalDemoSeedOperation>
{
    public void Configure(EntityTypeBuilder<ProfessionalDemoSeedOperation> builder)
    {
        builder.ToTable("ProfessionalDemoSeedOperations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScenarioKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OperationKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OperationType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.InputFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActorId).IsRequired();
        builder.Property(x => x.ResultDocumentId);
        builder.Property(x => x.StartedAt).HasPrecision(0).IsRequired();
        builder.Property(x => x.CompletedAt).HasPrecision(0);
        builder.HasIndex(x => new { x.ScenarioKey, x.OperationKey }).IsUnique();
    }
}
