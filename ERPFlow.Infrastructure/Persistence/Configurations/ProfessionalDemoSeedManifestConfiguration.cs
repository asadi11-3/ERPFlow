using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERPFlow.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalDemoSeedManifestConfiguration : IEntityTypeConfiguration<ProfessionalDemoSeedManifest>
{
    public void Configure(EntityTypeBuilder<ProfessionalDemoSeedManifest> builder)
    {
        builder.ToTable("ProfessionalDemoSeedManifests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScenarioKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CountsJson).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.ValidationJson).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.ScenarioKey, x.Version }).IsUnique();
    }
}
