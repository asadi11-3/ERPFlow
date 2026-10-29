using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ERPFlow.Tests;

public sealed class DevelopmentDataSeederIntegrationTests : IAsyncLifetime
{
    private const string Password = "isolated-seed-test-password";
    private readonly string databaseName = $"ERPFlow_SeedTests_{Guid.NewGuid():N}";
    private ERPFlowDbContext context = null!;

    public async Task InitializeAsync()
    {
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("ERPFLOW_TEST_SQLSERVER") ?? @".\SQLEXPRESS",
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true
        };
        context = new ERPFlowDbContext(new DbContextOptionsBuilder<ERPFlowDbContext>()
            .UseSqlServer(connection.ConnectionString).Options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await context.Database.EnsureDeletedAsync();
        await context.DisposeAsync();
    }

    [Fact]
    public async Task DevelopmentActivationConservesBalancesAndReplaysWithoutDuplicateOpenings()
    {
        await Seeder().SeedAsync("Development", Password);
        var environment = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(
            new Microsoft.AspNetCore.Builder.WebApplicationOptions { EnvironmentName = "Development" }).Environment;
        var activation = new ERPFlow.Api.DevelopmentInventory(context, environment);
        await activation.ActivateAsync();
        activation.EnsureWarehouseWritesAllowed();
        Assert.Equal(2898, await context.WarehouseStocks.SumAsync(s => s.StockOnHand));
        Assert.Equal(28, await context.WarehouseStocks.SumAsync(s => s.ReservedQuantity));
        var movements = await context.StockMovements.CountAsync();
        Assert.Equal(20, movements);
        await activation.ActivateAsync();
        Assert.Equal(movements, await context.StockMovements.CountAsync());
        environment.EnvironmentName = "Production";
        Assert.Throws<ERPFlow.Application.Common.WorkflowConflictException>(activation.EnsureOperatorExecutionAllowed);
        Assert.Throws<ERPFlow.Application.Common.WorkflowConflictException>(activation.EnsureWarehouseWritesAllowed);
    }

    [Fact]
    public async Task EmptyDatabaseGetsConsistentCompleteDemoAndSecondRunPreservesIt()
    {
        await Seeder().SeedAsync("Development", Password);
        context.ChangeTracker.Clear();
        Assert.Equal(8, await context.AppUsers.CountAsync());
        Assert.Equal(10, await context.Customers.CountAsync());
        Assert.Equal(8, await context.Customers.CountAsync(customer => customer.IsActive));
        Assert.Equal(20, await context.Products.CountAsync());
        Assert.Equal(30, await context.SalesOrderItems.CountAsync());
        Assert.Equal(23, await context.StockReservations.CountAsync());
        Assert.Equal(11, await context.StockReservations.CountAsync(reservation => !reservation.IsReleased));
        Assert.Equal(17, await context.ApprovalHistories.CountAsync());
        Assert.Equal(3, await context.DeliveryNotes.CountAsync());
        Assert.Equal(14, await context.MachineAssets.CountAsync());
        Assert.Equal(2, await context.Jobcards.CountAsync());
        Assert.Single(await context.JobcardItems.ToListAsync());
        Assert.Equal(3, await context.Warehouses.CountAsync(w => w.Code.StartsWith("DEMO-")));
        Assert.Equal(3, await context.Suppliers.CountAsync(s => s.SupplierCode.StartsWith("DEMO-")));
        var purchaseIds = await context.PurchaseOrders.OrderBy(o => o.Id).Select(o => o.Id).ToArrayAsync();
        Assert.Equal(10, purchaseIds.Length);
        var purchaseStatuses = await context.PurchaseOrders.GroupBy(o => o.Status).Select(g => g.Count()).ToArrayAsync();
        Assert.Equal(5, purchaseStatuses.Length);
        Assert.All(purchaseStatuses, count => Assert.Equal(2, count));
        Assert.Equal(14, await context.PurchaseOrderHistories.CountAsync());
        var transferIds = await context.StockTransfers.OrderBy(t => t.Id).Select(t => t.Id).ToArrayAsync();
        Assert.Equal(3, transferIds.Length);
        Assert.Equal(2, await context.StockTransfers.CountAsync(t => t.Status == ERPFlow.Domain.Transfers.StockTransferStatus.Draft));
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.StockMovements.ToListAsync());
        var orders = await context.SalesOrders.Include(order => order.Items)
            .Include(order => order.Reservations).AsSplitQuery().ToListAsync();
        Assert.Equal(4, orders.Count(order => order.Status == OrderStatus.Draft));
        Assert.Equal(5, orders.Count(order => order.Status == OrderStatus.PendingApproval));
        Assert.Equal(3, orders.Count(order => order.Status == OrderStatus.Approved));
        Assert.Equal(3, orders.Count(order => order.Status == OrderStatus.Rejected));
        var users = await context.AppUsers.ToDictionaryAsync(user => user.Id);
        var histories = await context.ApprovalHistories.OrderBy(history => history.CreatedAt).ToListAsync();
        foreach (var order in orders)
        {
            Assert.InRange(order.Items.Count, 1, 3);
            var history = histories.Where(entry => entry.SalesOrderId == order.Id).ToArray();
            var expected = order.Status switch
            {
                OrderStatus.Draft => Array.Empty<ApprovalAction>(),
                OrderStatus.PendingApproval => [ApprovalAction.Submitted],
                OrderStatus.Approved => [ApprovalAction.Submitted, ApprovalAction.Approved],
                OrderStatus.Rejected => [ApprovalAction.Submitted, ApprovalAction.Rejected],
                _ => throw new InvalidOperationException()
            };
            Assert.Equal(expected, history.Select(entry => entry.Action));
            if (history.Length > 0)
            {
                Assert.Equal(order.CreatedByUserId, history[0].UserId);
                Assert.Equal(UserRole.Sales, users[history[0].UserId].Role);
            }
            if (history.Length == 2)
            {
                Assert.True(users[history[1].UserId].Role is UserRole.Approver or UserRole.Administrator);
                Assert.True(history[0].CreatedAt <= history[1].CreatedAt);
                Assert.False(string.IsNullOrWhiteSpace(history[1].Comments));
            }
            Assert.All(order.Reservations, reservation => Assert.Equal(
                order.Status is OrderStatus.Approved or OrderStatus.Rejected,
                reservation.IsReleased));
            if (order.Status == OrderStatus.Approved)
            {
                Assert.Single(await context.DeliveryNotes.Where(note => note.SalesOrderId == order.Id).ToListAsync());
                Assert.Equal(
                    order.Items.Sum(item => item.Quantity),
                    await context.MachineAssets.CountAsync(asset => asset.SalesOrderId == order.Id));
            }
        }
        Assert.Equal(2898, await context.Products.SumAsync(product => product.StockOnHand));
        Assert.Equal(28, await context.Products.SumAsync(product => product.ReservedQuantity));
        await AssertReservationConsistencyAsync();

        var ids = orders.Select(order => order.Id).Order().ToArray();
        var hashes = users.Values.ToDictionary(user => user.Id, user => user.PasswordHash);
        var productState = await context.Products.ToDictionaryAsync(
            product => product.Id,
            product => new { product.StockOnHand, product.ReservedQuantity });
        await Seeder().SeedAsync("Development", "different-test-secret-must-not-replace-existing-hashes");
        Assert.Equal(purchaseIds, await context.PurchaseOrders.OrderBy(o => o.Id).Select(o => o.Id).ToArrayAsync());
        Assert.Equal(transferIds, await context.StockTransfers.OrderBy(t => t.Id).Select(t => t.Id).ToArrayAsync());
        Assert.Equal(14, await context.PurchaseOrderHistories.CountAsync());
        Assert.Equal(ids, (await context.SalesOrders.Select(order => order.Id).ToArrayAsync()).Order().ToArray());
        Assert.Equal(30, await context.SalesOrderItems.CountAsync());
        Assert.Equal(23, await context.StockReservations.CountAsync());
        Assert.Equal(17, await context.ApprovalHistories.CountAsync());
        Assert.Equal(3, await context.DeliveryNotes.CountAsync());
        Assert.Equal(14, await context.MachineAssets.CountAsync());
        Assert.Equal(2, await context.Jobcards.CountAsync());
        Assert.Single(await context.JobcardItems.ToListAsync());
        Assert.Equal(productState, await context.Products.ToDictionaryAsync(
            product => product.Id,
            product => new { product.StockOnHand, product.ReservedQuantity }));
        Assert.All(await context.AppUsers.ToListAsync(), user => Assert.Equal(hashes[user.Id], user.PasswordHash));
    }

    [Fact]
    public async Task RerunWithActiveServiceReservationStillCreatesMissingDemoOrdersAndPreservesConsistency()
    {
        var customer = new Customer("Service customer", "service-customer@example.test");
        var product = new Product("Existing scanner", "SCAN-100", 349m, 25);
        var jobcard = new Jobcard(
            "SRV-EXISTING-001",
            customer.Id,
            null,
            "Existing technician",
            "Existing service reservation",
            JobcardPriority.High);
        product.ReserveQuantity(2);
        jobcard.AddPart(product, 2);
        context.AddRange(customer, product, jobcard);
        await context.SaveChangesAsync();

        await Seeder().SeedAsync("Development", Password);
        await Seeder().SeedAsync("Development", Password);
        context.ChangeTracker.Clear();

        Assert.Equal(15, await context.SalesOrders.CountAsync());
        Assert.Single(await context.Jobcards.ToListAsync());
        Assert.Single(await context.JobcardItems.ToListAsync());
        var preserved = await context.Products.SingleAsync(candidate => candidate.Id == product.Id);
        Assert.Equal(25, preserved.StockOnHand);
        Assert.Equal(2, preserved.ReservedQuantity);
        Assert.Equal(11, await context.StockReservations.CountAsync(reservation => !reservation.IsReleased));
        Assert.Equal(3, await context.DeliveryNotes.CountAsync());
        Assert.Equal(14, await context.MachineAssets.CountAsync());
        await AssertReservationConsistencyAsync();
    }

    [Fact]
    public async Task ExistingBusinessRecordsAndConflictingReferencesAreNotOverwritten()
    {
        var customer = new Customer("User customer", "orders@contoso.local");
        customer.Deactivate();
        var product = new Product("User scanner", "SCAN-100", 777m, 0);
        var user = new AppUser("User", "user@example.test", UserRole.Sales, "existing-hash");
        var order = new SalesOrder(customer.Id, user.Id);
        order.AddItem(product, 1);
        var businessCustomer = new Customer("Business customer", "business@example.test");
        var businessProduct = new Product("Business machine", "BUSINESS-001", 500m, 5);
        var administrator = new AppUser("Business admin", "business-admin@example.test", UserRole.Administrator, "admin-hash");
        var approvedOrder = new SalesOrder(businessCustomer.Id, administrator.Id);
        approvedOrder.AddItem(businessProduct, 1);
        approvedOrder.Submit([businessProduct]);
        approvedOrder.Approve(administrator.Id);
        context.AddRange(
            customer, product, user, order,
            businessCustomer, businessProduct, administrator, approvedOrder);
        await context.SaveChangesAsync();

        await Seeder().SeedAsync("Development", Password);
        context.ChangeTracker.Clear();
        var preserved = await context.Products.SingleAsync(entity => entity.Id == product.Id);
        Assert.Equal("User scanner", preserved.Name);
        Assert.Equal(777m, preserved.UnitPrice);
        Assert.Equal(0, preserved.StockOnHand);
        Assert.False((await context.Customers.SingleAsync(entity => entity.Id == customer.Id)).IsActive);
        Assert.Equal(OrderStatus.Draft, (await context.SalesOrders.SingleAsync(entity => entity.Id == order.Id)).Status);
        Assert.Equal("existing-hash", (await context.AppUsers.SingleAsync(entity => entity.Id == user.Id)).PasswordHash);
        var preservedBusinessProduct = await context.Products.SingleAsync(entity => entity.Id == businessProduct.Id);
        Assert.Equal(5, preservedBusinessProduct.StockOnHand);
        Assert.Equal(1, preservedBusinessProduct.ReservedQuantity);
        Assert.False((await context.StockReservations.SingleAsync(
            reservation => reservation.SalesOrderId == approvedOrder.Id)).IsReleased);
        Assert.Empty(await context.DeliveryNotes.Where(note => note.SalesOrderId == approvedOrder.Id).ToListAsync());
        Assert.Empty(await context.MachineAssets.Where(asset => asset.SalesOrderId == approvedOrder.Id).ToListAsync());
    }

    [Theory]
    [InlineData("Production", "configured")]
    [InlineData("Staging", "configured")]
    [InlineData("Development", "")]
    [InlineData("Development", "  ")]
    public async Task SeederDoesNothingOutsideDevelopmentOrWithoutPassword(string environment, string password)
    {
        await Seeder().SeedAsync(environment, password);
        Assert.Equal(0, await context.AppUsers.CountAsync());
        Assert.Equal(0, await context.Customers.CountAsync());
        Assert.Equal(0, await context.Products.CountAsync());
        Assert.Equal(0, await context.SalesOrders.CountAsync());
    }

    [Fact]
    public async Task SeederHasNoProductionOverrideOrDefaultCredential()
    {
        await Seeder().SeedAsync("Production", Password);
        Assert.Empty(await context.AppUsers.ToArrayAsync());
        Assert.Empty(await context.Customers.ToArrayAsync());
        Assert.Null(typeof(DevelopmentDataSeeder).GetField("DefaultPassword"));
        Assert.DoesNotContain(typeof(DevelopmentDataSeeder).GetMethod("SeedAsync")!.GetParameters(), p => p.Name == "allowProduction");
    }

    private async Task AssertReservationConsistencyAsync()
    {
        var activeStatuses = new[]
        {
            JobcardStatus.Open,
            JobcardStatus.PartsRequested,
            JobcardStatus.InProgress
        };
        foreach (var product in await context.Products.ToListAsync())
        {
            var salesTotal = await context.StockReservations.Where(reservation =>
                    reservation.ProductId == product.Id && !reservation.IsReleased)
                .SumAsync(reservation => reservation.Quantity);
            var serviceTotal = await context.JobcardItems
                .Join(
                    context.Jobcards.Where(jobcard => activeStatuses.Contains(jobcard.Status)),
                    item => item.JobcardId,
                    jobcard => jobcard.Id,
                    (item, _) => item)
                .Where(item => item.ProductId == product.Id)
                .SumAsync(item => item.Quantity);
            Assert.Equal(salesTotal + serviceTotal, product.ReservedQuantity);
            Assert.InRange(product.ReservedQuantity, 0, product.StockOnHand);
        }
    }

    private DevelopmentDataSeeder Seeder() => new(context, NullLogger<DevelopmentDataSeeder>.Instance);
}
