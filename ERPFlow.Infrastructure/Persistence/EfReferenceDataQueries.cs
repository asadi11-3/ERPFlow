using ERPFlow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

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
        CancellationToken cancellationToken = default) =>
        await context.Products
            .AsNoTracking()
            .OrderBy(product => product.Name)
            .Select(product => new ProductReference(
                product.Id,
                product.Name,
                product.Sku,
                product.UnitPrice,
                product.StockOnHand - product.ReservedQuantity))
            .ToListAsync(cancellationToken);
}
