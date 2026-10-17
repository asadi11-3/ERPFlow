using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class SalesOrderWorkflowIntegrationTests
{
    [Fact]
    public async Task WorkflowUsesServerPricesAndPersistsSubmissionAndApprovalAudits()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = new Customer("Active Customer", "active@example.test");
        var product = new Product("Product", "PRODUCT-1", 42.50m, 10);
        var user = new AppUser("Approver", "approver@example.test", UserRole.Approver);
        context.AddRange(customer, product, user);
        await context.SaveChangesAsync();

        var workflow = CreateWorkflow(context, user.Id);
        var draft = await workflow.CreateDraftAsync(
            new CreateSalesOrderCommand(customer.Id, [new CreateSalesOrderItem(product.Id, 3)]));

        Assert.Equal(OrderStatus.Draft, draft.Status);
        Assert.Equal(42.50m, Assert.Single(draft.Items).UnitPrice);

        var submitted = await workflow.SubmitAsync(draft.Id);
        Assert.Equal(OrderStatus.PendingApproval, submitted.Status);
        Assert.Equal(3, Assert.Single(submitted.Reservations).Quantity);

        var pending = await workflow.ListPendingApprovalsAsync();
        Assert.Single(pending);
        Assert.Single(pending[0].Items);
        Assert.Single(pending[0].Reservations);

        var approved = await workflow.ApproveAsync(draft.Id, "Approved for fulfilment.");
        Assert.Equal(OrderStatus.Approved, approved.Status);
        Assert.False(Assert.Single(approved.Reservations).IsReleased);

        var persistedProduct = await context.Products.SingleAsync(entity => entity.Id == product.Id);
        Assert.Equal(3, persistedProduct.ReservedQuantity);

        var audits = await context.ApprovalHistories
            .Where(history => history.SalesOrderId == draft.Id)
            .OrderBy(history => history.CreatedAt)
            .ToListAsync();
        Assert.Equal([ApprovalAction.Submitted, ApprovalAction.Approved], audits.Select(audit => audit.Action));
        Assert.All(audits, audit => Assert.Equal(user.Id, audit.UserId));
    }

    [Fact]
    public async Task RejectReleasesReservationOnceAndPersistsReason()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = new Customer("Active Customer", "active@example.test");
        var product = new Product("Product", "PRODUCT-1", 15m, 5);
        var user = new AppUser("Approver", "approver@example.test", UserRole.Approver);
        context.AddRange(customer, product, user);
        await context.SaveChangesAsync();

        var workflow = CreateWorkflow(context, user.Id);
        var draft = await workflow.CreateDraftAsync(
            new CreateSalesOrderCommand(customer.Id, [new CreateSalesOrderItem(product.Id, 4)]));
        await workflow.SubmitAsync(draft.Id);

        var rejected = await workflow.RejectAsync(draft.Id, "Customer credit review failed.");

        Assert.Equal(OrderStatus.Rejected, rejected.Status);
        Assert.True(Assert.Single(rejected.Reservations).IsReleased);
        Assert.Equal(0, (await context.Products.SingleAsync(entity => entity.Id == product.Id)).ReservedQuantity);
        var audit = await context.ApprovalHistories
            .SingleAsync(history => history.SalesOrderId == draft.Id && history.Action == ApprovalAction.Rejected);
        Assert.Equal("Customer credit review failed.", audit.Comments);
        Assert.Equal(user.Id, audit.UserId);

        await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            workflow.RejectAsync(draft.Id, "Second rejection."));
    }

    [Fact]
    public async Task InactiveCustomerCannotReceiveDraftOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = new Customer("Inactive Customer", "inactive@example.test");
        customer.Deactivate();
        var product = new Product("Product", "PRODUCT-1", 10m, 5);
        context.AddRange(customer, product);
        await context.SaveChangesAsync();

        var workflow = CreateWorkflow(context, Guid.NewGuid());

        await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            workflow.CreateDraftAsync(
                new CreateSalesOrderCommand(customer.Id, [new CreateSalesOrderItem(product.Id, 1)])));
    }

    [Fact]
    public async Task ConcurrentDuplicateSubmissionIsRejectedByRowVersion()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedDraftOrdersAsync(database, orderCount: 1, stockOnHand: 5, quantity: 4);
        {
            await using var firstContext = database.CreateContext();
            await using var secondContext = database.CreateContext();
            var firstPersistence = new EfSalesOrderPersistence(firstContext);
            var secondPersistence = new EfSalesOrderPersistence(secondContext);
            await using var firstTransaction = await firstPersistence.BeginTransactionAsync();
            await using var secondTransaction = await secondPersistence.BeginTransactionAsync();
            var firstOrder = await firstPersistence.GetOrderAsync(seed.OrderIds[0], true) ?? throw new InvalidOperationException();
            var secondOrder = await secondPersistence.GetOrderAsync(seed.OrderIds[0], true) ?? throw new InvalidOperationException();
            var firstProducts = await firstPersistence.GetProductsAsync([seed.ProductId]);
            var secondProducts = await secondPersistence.GetProductsAsync([seed.ProductId]);
            firstOrder.Submit(firstProducts);
            secondOrder.Submit(secondProducts);

            await firstPersistence.SaveChangesAsync();
            await firstTransaction.CommitAsync();

            var exception = await Record.ExceptionAsync(() => secondPersistence.SaveChangesAsync());
            Assert.True(exception is ConcurrencyConflictException or DuplicateReservationException);
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(4, (await verificationContext.Products.SingleAsync()).ReservedQuantity);
        Assert.Single(await verificationContext.StockReservations.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentOrdersCannotOversellStock()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedDraftOrdersAsync(database, orderCount: 2, stockOnHand: 5, quantity: 4);
        {
            await using var firstContext = database.CreateContext();
            await using var secondContext = database.CreateContext();
            var firstPersistence = new EfSalesOrderPersistence(firstContext);
            var secondPersistence = new EfSalesOrderPersistence(secondContext);
            await using var firstTransaction = await firstPersistence.BeginTransactionAsync();
            await using var secondTransaction = await secondPersistence.BeginTransactionAsync();
            var firstOrder = await firstPersistence.GetOrderAsync(seed.OrderIds[0], true) ?? throw new InvalidOperationException();
            var secondOrder = await secondPersistence.GetOrderAsync(seed.OrderIds[1], true) ?? throw new InvalidOperationException();
            var firstProducts = await firstPersistence.GetProductsAsync([seed.ProductId]);
            var secondProducts = await secondPersistence.GetProductsAsync([seed.ProductId]);
            firstOrder.Submit(firstProducts);
            secondOrder.Submit(secondProducts);

            await firstPersistence.SaveChangesAsync();
            await firstTransaction.CommitAsync();

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondPersistence.SaveChangesAsync());
        }

        await using var verificationContext = database.CreateContext();
        var persistedProduct = await verificationContext.Products.SingleAsync();
        Assert.Equal(4, persistedProduct.ReservedQuantity);
        Assert.Equal(1, await verificationContext.StockReservations.CountAsync());
    }

    private static SalesOrderWorkflow CreateWorkflow(ERPFlowDbContext context, Guid userId) =>
        new(new EfSalesOrderPersistence(context), new TestCurrentUser(userId));

    private static async Task<SeedResult> SeedDraftOrdersAsync(
        SqlServerTestDatabase database,
        int orderCount,
        int stockOnHand,
        int quantity)
    {
        await using var context = database.CreateContext();
        var customer = new Customer("Customer", "customer@example.test");
        var product = new Product("Product", "PRODUCT-1", 10m, stockOnHand);
        context.AddRange(customer, product);
        var orders = Enumerable.Range(0, orderCount)
            .Select(_ =>
            {
                var order = new SalesOrder(customer.Id);
                order.AddItem(product, quantity);
                return order;
            })
            .ToArray();
        context.SalesOrders.AddRange(orders);
        await context.SaveChangesAsync();
        return new SeedResult(product.Id, orders.Select(order => order.Id).ToArray());
    }

    private sealed record SeedResult(Guid ProductId, IReadOnlyList<Guid> OrderIds);

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
    }

    private sealed class SqlServerTestDatabase(string connectionString) : IAsyncDisposable
    {
        public static async Task<SqlServerTestDatabase> CreateAsync()
        {
            var server = Environment.GetEnvironmentVariable("ERPFLOW_TEST_SQLSERVER") ?? @".\SQLEXPRESS";
            var databaseName = $"ERPFlow_Phase3Tests_{Guid.NewGuid():N}";
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = databaseName,
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true
            }.ConnectionString;
            var database = new SqlServerTestDatabase(connectionString);
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }

        public ERPFlowDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ERPFlowDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            return new ERPFlowDbContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
