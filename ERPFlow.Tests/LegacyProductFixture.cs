using ERPFlow.Domain.Entities;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Tests;

internal static class LegacyProductFixture
{
    internal static async Task<bool> InsertIfLegacyAsync(ERPFlowDbContext context, Product product)
    {
        var hasCatalog = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sys.columns WHERE object_id=OBJECT_ID(N'Products') AND name=N'IsActive'").SingleAsync() != 0;
        if (hasCatalog) return false;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Products(Id,Name,Sku,UnitPrice,StockOnHand,ReservedQuantity,CreatedAt)
            VALUES({product.Id},{product.Name},{product.Sku},{product.UnitPrice},{product.StockOnHand},{product.ReservedQuantity},{product.CreatedAt});
            """);
        var version = await context.Database.SqlQuery<byte[]>($"SELECT RowVersion AS Value FROM Products WHERE Id={product.Id}").SingleAsync();
        context.Entry(product).Property(x => x.RowVersion).CurrentValue = version;
        return true;
    }
}
