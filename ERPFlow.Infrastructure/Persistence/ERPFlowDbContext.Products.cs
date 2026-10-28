using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ERPFlowDbContext
{
    public DbSet<ProductCatalogChange> ProductCatalogChanges => Set<ProductCatalogChange>();
}
