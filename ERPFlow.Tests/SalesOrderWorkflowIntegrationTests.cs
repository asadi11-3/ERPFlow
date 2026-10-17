using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
        var user = new AppUser("Administrator", "admin@example.test", UserRole.Administrator, "test-hash");
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
        var user = new AppUser("Administrator", "admin@example.test", UserRole.Administrator, "test-hash");
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
        var user = new AppUser("Sales", "sales@example.test", UserRole.Sales, "test-hash");
        context.AddRange(customer, product, user);
        await context.SaveChangesAsync();

        var workflow = CreateWorkflow(context, user.Id);

        await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            workflow.CreateDraftAsync(
                new CreateSalesOrderCommand(customer.Id, [new CreateSalesOrderItem(product.Id, 1)])));
    }

    [Fact]
    public async Task SalesUserCannotAccessAnotherSalesUsersOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid orderId;
        Guid secondUserId;

        await using (var context = database.CreateContext())
        {
            var customer = new Customer("Customer", "customer@example.test");
            var product = new Product("Product", "PRODUCT-1", 10m, 5);
            var firstUser = new AppUser("First Sales", "first@example.test", UserRole.Sales, "test-hash");
            var secondUser = new AppUser("Second Sales", "second@example.test", UserRole.Sales, "test-hash");
            context.AddRange(customer, product, firstUser, secondUser);
            await context.SaveChangesAsync();

            var order = await CreateWorkflow(context, firstUser.Id).CreateDraftAsync(
                new CreateSalesOrderCommand(customer.Id, [new CreateSalesOrderItem(product.Id, 1)]));
            orderId = order.Id;
            secondUserId = secondUser.Id;
        }

        await using var secondContext = database.CreateContext();
        var workflow = CreateWorkflow(secondContext, secondUserId);
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => workflow.GetByIdAsync(orderId));
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => workflow.SubmitAsync(orderId));
    }

    [Fact]
    public async Task ApplicationWorkflowEnforcesPersistedUserRoles()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = new Customer("Customer", "customer@example.test");
        var product = new Product("Product", "PRODUCT-1", 10m, 5);
        var salesUser = new AppUser("Sales", "sales@example.test", UserRole.Sales, "test-hash");
        var approver = new AppUser("Approver", "approver@example.test", UserRole.Approver, "test-hash");
        context.AddRange(customer, product, salesUser, approver);
        await context.SaveChangesAsync();

        var command = new CreateSalesOrderCommand(
            customer.Id,
            [new CreateSalesOrderItem(product.Id, 1)]);
        var approverWorkflow = CreateWorkflow(context, approver.Id);
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() =>
            approverWorkflow.CreateDraftAsync(command));

        var salesWorkflow = CreateWorkflow(context, salesUser.Id);
        var draft = await salesWorkflow.CreateDraftAsync(command);
        await salesWorkflow.SubmitAsync(draft.Id);
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() =>
            salesWorkflow.ApproveAsync(draft.Id));
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() =>
            salesWorkflow.ListPendingApprovalsAsync());
    }

    [Fact]
    public async Task Phase4MigrationBackfillsLegacyOrdersWithoutDataLoss()
    {
        const string phase3Migration = "20261017092052_Phase3ApplicationWorkflow";
        await using var database = await SqlServerTestDatabase.CreateAsync(phase3Migration);
        await using var context = database.CreateContext();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO AppUsers (Id, Name, Email, Role, CreatedAt)
            VALUES ({userId}, N'Legacy User', N'legacy@example.test', 1, SYSUTCDATETIME());
            INSERT INTO Customers (Id, Name, Email, CreatedAt, IsActive)
            VALUES ({customerId}, N'Legacy Customer', N'customer@example.test', SYSUTCDATETIME(), 1);
            INSERT INTO SalesOrders (Id, CustomerId, Status, CreatedAt, SubmittedAt, DecidedAt)
            VALUES ({orderId}, {customerId}, 0, SYSUTCDATETIME(), NULL, NULL);
            """);

        await context.GetService<IMigrator>().MigrateAsync();
        context.ChangeTracker.Clear();

        var order = await context.SalesOrders.SingleAsync(entity => entity.Id == orderId);
        var owner = await context.AppUsers.SingleAsync(entity => entity.Id == order.CreatedByUserId);
        var existingUser = await context.AppUsers.SingleAsync(entity => entity.Id == userId);
        Assert.Equal("Legacy Order Owner", owner.Name);
        Assert.Equal("!PASSWORD-RESET-REQUIRED!", owner.PasswordHash);
        Assert.Equal("!PASSWORD-RESET-REQUIRED!", existingUser.PasswordHash);
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
        var user = new AppUser("Sales", "sales@example.test", UserRole.Sales, "test-hash");
        context.AddRange(customer, product, user);
        var orders = Enumerable.Range(0, orderCount)
            .Select(_ =>
            {
                var order = new SalesOrder(customer.Id, user.Id);
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
        public static async Task<SqlServerTestDatabase> CreateAsync(string? targetMigration = null)
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
            await context.GetService<IMigrator>().MigrateAsync(targetMigration);
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
