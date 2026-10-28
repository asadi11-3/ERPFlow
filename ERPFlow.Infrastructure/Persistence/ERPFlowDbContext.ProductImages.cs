using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ERPFlowDbContext
{
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductImageObject> ProductImageObjects => Set<ProductImageObject>();
    public DbSet<ProductImageStore> ProductImageStores => Set<ProductImageStore>();
}
