using System.Data;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfSalesOrderPersistence(ERPFlowDbContext context) : ISalesOrderPersistence
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        return new EfApplicationTransaction(transaction);
    }

    public Task<Customer?> FindCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default) =>
        context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(customer => customer.Id == customerId, cancellationToken);

    public Task<AppUser?> FindAppUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default) =>
        await context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

    public async Task<SalesOrder?> GetOrderAsync(
        Guid orderId,
        bool tracking,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SalesOrder> query = context.SalesOrders;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Include(order => order.Items)
            .Include(order => order.Reservations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);
    }

    public Task<Guid?> FindOrderOwnerIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default) =>
        context.SalesOrders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => (Guid?)order.CreatedByUserId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesOrder>> ListPendingOrdersAsync(
        CancellationToken cancellationToken = default) =>
        await context.SalesOrders
            .AsNoTracking()
            .Where(order => order.Status == OrderStatus.PendingApproval)
            .Include(order => order.Items)
            .Include(order => order.Reservations)
            .AsSplitQuery()
            .OrderBy(order => order.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApprovalHistory>> ListApprovalHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default) =>
        await context.ApprovalHistories
            .AsNoTracking()
            .Where(history => history.SalesOrderId == orderId)
            .OrderBy(history => history.CreatedAt)
            .ThenBy(history => history.Id)
            .ToListAsync(cancellationToken);

    public void AddOrder(SalesOrder order) => context.SalesOrders.Add(order);

    public void AddApprovalHistory(ApprovalHistory history) =>
        context.ApprovalHistories.Add(history);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var entityNames = string.Join(", ", exception.Entries.Select(entry => entry.Metadata.ClrType.Name));
            throw new ConcurrencyConflictException(
                $"The sales order operation conflicted while saving: {entityNames}. Retry the operation.",
                exception);
        }
        catch (DbUpdateException exception) when (FindSqlException(exception)?.Number is 2601 or 2627)
        {
            throw new DuplicateReservationException(
                "A product has already been reserved for this sales order.",
                exception);
        }
        catch (DbUpdateException exception) when (FindSqlException(exception)?.Number == 1205)
        {
            throw new ConcurrencyConflictException(
                "The sales order operation conflicted with another transaction. Retry the operation.",
                exception);
        }
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException;
            }
        }

        return null;
    }
}
