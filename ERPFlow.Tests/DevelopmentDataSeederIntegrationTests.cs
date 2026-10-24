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
    public async Task EmptyDatabaseGetsConsistentCompleteDemoAndSecondRunPreservesIt()
    {
        await Seeder().SeedAsync("Development", Password);
        context.ChangeTracker.Clear();
        Assert.Equal(6, await context.AppUsers.CountAsync());
        Assert.Equal(10, await context.Customers.CountAsync());
        Assert.Equal(8, await context.Customers.CountAsync(customer => customer.IsActive));
        Assert.Equal(20, await context.Products.CountAsync());
        Assert.Equal(30, await context.SalesOrderItems.CountAsync());
        Assert.Equal(23, await context.StockReservations.CountAsync());
        Assert.Equal(17, await context.StockReservations.CountAsync(reservation => !reservation.IsReleased));
        Assert.Equal(17, await context.ApprovalHistories.CountAsync());
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
            Assert.All(order.Reservations, reservation =>
                Assert.Equal(order.Status == OrderStatus.Rejected, reservation.IsReleased));
        }
        foreach (var product in await context.Products.ToListAsync())
        {
            var total = await context.StockReservations.Where(reservation =>
                    reservation.ProductId == product.Id && !reservation.IsReleased)
                .SumAsync(reservation => reservation.Quantity);
            Assert.Equal(total, product.ReservedQuantity);
            Assert.InRange(total, 0, product.StockOnHand);
        }

        var ids = orders.Select(order => order.Id).Order().ToArray();
        var hashes = users.Values.ToDictionary(user => user.Id, user => user.PasswordHash);
        await Seeder().SeedAsync("Development", "different-test-secret-must-not-replace-existing-hashes");
        Assert.Equal(ids, (await context.SalesOrders.Select(order => order.Id).ToArrayAsync()).Order().ToArray());
        Assert.Equal(30, await context.SalesOrderItems.CountAsync());
        Assert.Equal(23, await context.StockReservations.CountAsync());
        Assert.Equal(17, await context.ApprovalHistories.CountAsync());
        Assert.All(await context.AppUsers.ToListAsync(), user => Assert.Equal(hashes[user.Id], user.PasswordHash));
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
        context.AddRange(customer, product, user, order);
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

    private DevelopmentDataSeeder Seeder() => new(context, NullLogger<DevelopmentDataSeeder>.Instance);
}
