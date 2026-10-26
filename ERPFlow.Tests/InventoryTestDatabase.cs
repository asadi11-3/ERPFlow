using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Collections.Concurrent;

namespace ERPFlow.Tests;

internal sealed class InventoryTestDatabase(string connectionString) : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, byte> OwnedConnections = new(StringComparer.Ordinal);

    internal static bool Owns(ERPFlowDbContext context) => OwnedConnections.ContainsKey(
        Identity(context.Database.GetConnectionString()));

    private static string Identity(string? connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        return $"{connection.DataSource.Trim().ToUpperInvariant()}|{connection.InitialCatalog.Trim().ToUpperInvariant()}";
    }
    public const string LegacyMigration = "20261024110336_CriticalWorkflowConsistency";

    public static async Task<InventoryTestDatabase> CreateAsync(string? targetMigration = null)
    {
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("ERPFLOW_TEST_SQLSERVER") ?? @".\SQLEXPRESS",
            InitialCatalog = $"ERPFlow_InventoryFoundation_{Guid.NewGuid():N}",
            IntegratedSecurity = true,
            TrustServerCertificate = true
        };
        var database = new InventoryTestDatabase(connection.ConnectionString);
        try
        {
            await using var context = database.Context();
            await context.GetService<IMigrator>().MigrateAsync(targetMigration);
            OwnedConnections.TryAdd(Identity(connection.ConnectionString), 0);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public ERPFlowDbContext Context(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ERPFlowDbContext>().UseSqlServer(connectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new ERPFlowDbContext(options.Options);
    }

    public async Task<InventorySeed> SeedAsync()
    {
        await using var context = Context();
        var actor = new AppUser("Inventory test", "inventory@example.test", UserRole.Administrator, "test-hash");
        var customer = new Customer("Customer", "inventory@example.test");
        var product = new Product("Product", "INVENTORY", 10m, 50);
        var first = new SalesOrder(customer.Id, actor.Id);
        first.AddItem(product, 3);
        first.Submit([product]);
        var second = new SalesOrder(customer.Id, actor.Id);
        second.AddItem(product, 3);
        var jobcard = new Jobcard("INV-TEST", customer.Id, null, "Tech", "Fault");
        product.ReserveQuantity(3);
        jobcard.AddPart(product, 3);
        var warehouse = new Warehouse("MAIN", "Main");
        var otherWarehouse = new Warehouse("OTHER", "Other");
        var stock = new WarehouseStock(warehouse.Id, product.Id);
        var otherStock = new WarehouseStock(otherWarehouse.Id, product.Id);
        context.AddRange(actor, customer, product, first, second, jobcard, warehouse, otherWarehouse, stock, otherStock);
        await context.SaveChangesAsync();
        return new InventorySeed(actor.Id, product.Id, warehouse.Id, otherWarehouse.Id, stock.Id, otherStock.Id,
            first.Id, second.Id, jobcard.Items[0].Id, stock.RowVersion.ToArray(), otherStock.RowVersion.ToArray());
    }

    public static IInventoryPosting Poster(ERPFlowDbContext context, Guid actorId) =>
        new EfInventoryPosting(context, new IsolatedTestWriteGate(), new TestActor(actorId));

    public async ValueTask DisposeAsync()
    {
        OwnedConnections.TryRemove(Identity(connectionString), out _);
        SqlConnection.ClearAllPools();
        await using var context = Context();
        await context.Database.EnsureDeletedAsync();
    }

    private sealed class IsolatedTestWriteGate : IInventoryWriteGate
    {
        public void EnsureWarehouseWritesAllowed() { }
    }

    internal sealed class TestActor(Guid id) : ICurrentUser
    {
        public Guid UserId => id;
    }
}

internal sealed record InventorySeed(Guid ActorId, Guid ProductId, Guid WarehouseId, Guid OtherWarehouseId,
    Guid StockId, Guid OtherStockId, Guid SalesOrderId, Guid OtherOrderId, Guid JobcardItemId,
    byte[] Version, byte[] OtherVersion)
{
    public InventoryPostingRequest Opening(int quantity = 10, string key = "opening") =>
        new(key, InventoryPostingKind.Opening, WarehouseId, ProductId, quantity, Version);

    public InventoryPostingRequest Reserve(byte[] version, string key = "reserve", Guid? owner = null) =>
        new(key, InventoryPostingKind.Reserve, WarehouseId, ProductId, 3, version, owner ?? SalesOrderId);
}
