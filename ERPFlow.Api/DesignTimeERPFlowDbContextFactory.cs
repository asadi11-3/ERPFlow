using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERPFlow.Api;

public sealed class DesignTimeERPFlowDbContextFactory : IDesignTimeDbContextFactory<ERPFlowDbContext>
{
    public ERPFlowDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ERPFlowDbContext>()
        .UseSqlServer("Server=unreachable;Database=DesignOnly;Integrated Security=True").Options);
}
