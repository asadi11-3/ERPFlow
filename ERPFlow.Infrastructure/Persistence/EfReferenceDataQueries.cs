using ERPFlow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence.Inventory;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfReferenceDataQueries(ERPFlowDbContext context) : IReferenceDataQueries
{
    public async Task<IReadOnlyList<CustomerReference>> ListActiveCustomersAsync(
        CancellationToken cancellationToken = default) =>
        await context.Customers
            .AsNoTracking()
            .Where(customer => customer.IsActive)
            .OrderBy(customer => customer.Name)
            .Select(customer => new CustomerReference(customer.Id, customer.Name, customer.Email))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductReference>> ListProductsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await InventoryAuthoritySession.BeginAsync(context, cancellationToken);
        var warehouse = context.ObservedInventoryAuthority == InventoryAuthorityMode.Warehouse;
        var results = await context.Products
            .AsNoTracking()
            .OrderBy(product => product.Name)
            .Select(product => new ProductReference(
                product.Id,
                product.Name,
                product.Sku,
                product.UnitPrice,
                warehouse ? context.WarehouseStocks.Where(stock => stock.ProductId == product.Id && stock.WarehouseId == LegacyDefaultWarehouse.Id)
                    .Select(stock => stock.StockOnHand - stock.ReservedQuantity).FirstOrDefault() : product.StockOnHand - product.ReservedQuantity))
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return results;
    }
}
