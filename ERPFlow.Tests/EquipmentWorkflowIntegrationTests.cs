using System.Data.Common;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Equipment;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class EquipmentWorkflowIntegrationTests
{
    [Fact]
    public async Task MigrationRejectsHistoricalDuplicateDeliveriesWithoutDeletingThem()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync("20261024083830_Phase5EnterpriseShowcase");
        var seed = await SeedApprovedOrderAsync(database, quantity: 1);
        await using var context = database.CreateContext();
        context.DeliveryNotes.AddRange(
            new DeliveryNote(seed.OrderId, seed.CustomerId, "LEGACY-FIRST", "Carrier"),
            new DeliveryNote(seed.OrderId, seed.CustomerId, "LEGACY-SECOND", "Carrier"));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.MigrateAsync());

        Assert.Equal(1505, exception.Number);
        Assert.Equal(2, await context.DeliveryNotes.CountAsync());
        Assert.DoesNotContain("20261024110336_CriticalWorkflowConsistency", await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task MigrationPreservesExistingDispatchAndDatabaseRejectsSecondDeliveryForOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync("20261024083830_Phase5EnterpriseShowcase");
        var seed = await SeedApprovedOrderAsync(database, quantity: 1);
        Guid originalDeliveryId;
        await using (var context = database.CreateContext())
        {
            var delivery = new DeliveryNote(seed.OrderId, seed.CustomerId, "LEGACY-DELIVERY", "Carrier");
            context.DeliveryNotes.Add(delivery);
            await context.SaveChangesAsync();
            originalDeliveryId = delivery.Id;
            await context.Database.MigrateAsync();
        }

        await using (var context = database.CreateContext())
        {
            context.DeliveryNotes.Add(new DeliveryNote(seed.OrderId, seed.CustomerId, "DIFFERENT-DELIVERY", "Other carrier"));
            await Assert.ThrowsAsync<WorkflowConflictException>(() => new EfEquipmentPersistence(context).SaveChangesAsync());
        }

        await using var verification = database.CreateContext();
        Assert.Equal(originalDeliveryId, (await verification.DeliveryNotes.SingleAsync()).Id);
        var product = await verification.Products.SingleAsync();
        Assert.Equal(10, product.StockOnHand);
        Assert.Equal(1, product.ReservedQuantity);
    }

    [Fact]
    public async Task ApprovedDispatchConsumesStockAndPersistsOneDeliveryAndAssetsOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedApprovedOrderAsync(database, quantity: 2);

        await using (var context = database.CreateContext())
        {
            var workflow = CreateWorkflow(context, seed.TechnicianId);
            var result = await workflow.DispatchOrderAsync(
                seed.OrderId,
                new DispatchOrderCommand(
                    "Carrier",
                    "TRACK-1",
                    [
                        new MachineSerialInput(seed.ProductId, " serial-1 "),
                        new MachineSerialInput(seed.ProductId, "serial-2")
                    ]));

            Assert.Equal(seed.OrderId, result.SalesOrderId);
            await Assert.ThrowsAsync<WorkflowConflictException>(() =>
                workflow.DispatchOrderAsync(seed.OrderId, new DispatchOrderCommand("Carrier", null, null)));
        }

        await using var verification = database.CreateContext();
        var product = await verification.Products.SingleAsync(entity => entity.Id == seed.ProductId);
        var reservation = await verification.StockReservations.SingleAsync(entity => entity.SalesOrderId == seed.OrderId);
        Assert.Equal(8, product.StockOnHand);
        Assert.Equal(0, product.ReservedQuantity);
        Assert.True(reservation.IsReleased);
        Assert.Single(await verification.DeliveryNotes.Where(note => note.SalesOrderId == seed.OrderId).ToListAsync());
        var serials = await verification.MachineAssets
            .Where(asset => asset.SalesOrderId == seed.OrderId)
            .Select(asset => asset.SerialNumber)
            .OrderBy(serial => serial)
            .ToListAsync();
        Assert.Equal(["SERIAL-1", "SERIAL-2"], serials);
    }

    [Fact]
    public async Task SuppliedSerialsMustExactlyAndUniquelyCoverTheOrderBeforeStockChanges()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedApprovedOrderAsync(database, quantity: 2);
        await using var context = database.CreateContext();
        var workflow = CreateWorkflow(context, seed.TechnicianId);

        var invalidCommands = new[]
        {
            new DispatchOrderCommand("Carrier", null, [null!]),
            new DispatchOrderCommand("Carrier", null, [new MachineSerialInput(seed.ProductId, "one")]),
            new DispatchOrderCommand("Carrier", null,
            [
                new MachineSerialInput(seed.ProductId, "same"),
                new MachineSerialInput(seed.ProductId, " SAME ")
            ]),
            new DispatchOrderCommand("Carrier", null,
            [
                new MachineSerialInput(seed.ProductId, "valid"),
                new MachineSerialInput(seed.ProductId, " ")
            ]),
            new DispatchOrderCommand("Carrier", null,
            [
                new MachineSerialInput(seed.ProductId, "valid"),
                new MachineSerialInput(Guid.NewGuid(), "other")
            ])
        };

        foreach (var command in invalidCommands)
        {
            await Assert.ThrowsAsync<WorkflowValidationException>(() =>
                workflow.DispatchOrderAsync(seed.OrderId, command));
        }

        context.ChangeTracker.Clear();
        var product = await context.Products.SingleAsync(entity => entity.Id == seed.ProductId);
        Assert.Equal(10, product.StockOnHand);
        Assert.Equal(2, product.ReservedQuantity);
        Assert.Empty(await context.DeliveryNotes.ToListAsync());
        Assert.Empty(await context.MachineAssets.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentWorkflowDispatchesPersistOnlyOneWholeDispatch()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedApprovedOrderAsync(database, quantity: 2);
        var barrier = new ProductReadBarrier();
        await using var firstContext = database.CreateContext(barrier);
        await using var secondContext = database.CreateContext(barrier);
        var firstWorkflow = CreateWorkflow(firstContext, seed.TechnicianId);
        var secondWorkflow = CreateWorkflow(secondContext, seed.TechnicianId);
        var command = new DispatchOrderCommand("Carrier", null, null);

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => firstWorkflow.DispatchOrderAsync(seed.OrderId, command)),
            CaptureAsync(() => secondWorkflow.DispatchOrderAsync(seed.OrderId, command)));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is WorkflowConflictException or ConcurrencyConflictException);

        await using var verification = database.CreateContext();
        var product = await verification.Products.SingleAsync(entity => entity.Id == seed.ProductId);
        Assert.Equal(8, product.StockOnHand);
        Assert.Equal(0, product.ReservedQuantity);
        Assert.Single(await verification.DeliveryNotes.Where(note => note.SalesOrderId == seed.OrderId).ToListAsync());
        Assert.Equal(2, await verification.MachineAssets.CountAsync(asset => asset.SalesOrderId == seed.OrderId));
    }

    [Fact]
    public async Task SqlFailureRollsBackStockReservationDeliveryAndNewAssets()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedApprovedOrderAsync(database, quantity: 1);
        await using (var setup = database.CreateContext())
        {
            setup.MachineAssets.Add(new MachineAsset(
                "EXISTING-TAG",
                "COLLIDING-SERIAL",
                seed.ProductId,
                seed.CustomerId,
                null,
                DateTime.UtcNow.AddYears(1)));
            await setup.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var workflow = CreateWorkflow(context, seed.TechnicianId);
            await Assert.ThrowsAsync<WorkflowConflictException>(() =>
                workflow.DispatchOrderAsync(
                    seed.OrderId,
                    new DispatchOrderCommand(
                        "Carrier",
                        null,
                        [new MachineSerialInput(seed.ProductId, "colliding-serial")])));
        }

        await using var verification = database.CreateContext();
        var product = await verification.Products.SingleAsync(entity => entity.Id == seed.ProductId);
        var reservation = await verification.StockReservations.SingleAsync(entity => entity.SalesOrderId == seed.OrderId);
        Assert.Equal(10, product.StockOnHand);
        Assert.Equal(1, product.ReservedQuantity);
        Assert.False(reservation.IsReleased);
        Assert.Empty(await verification.DeliveryNotes.Where(note => note.SalesOrderId == seed.OrderId).ToListAsync());
        Assert.Single(await verification.MachineAssets.ToListAsync());
    }

    [Fact]
    public async Task DispatchRequiresPersistedTechnicianOrAdministratorRole()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedApprovedOrderAsync(database, quantity: 1);
        Guid salesUserId;
        await using (var setup = database.CreateContext())
        {
            var salesUser = new AppUser("Sales", "sales@example.test", UserRole.Sales, "test-hash");
            setup.AppUsers.Add(salesUser);
            await setup.SaveChangesAsync();
            salesUserId = salesUser.Id;
        }

        await using var context = database.CreateContext();
        var workflow = CreateWorkflow(context, salesUserId);
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() =>
            workflow.DispatchOrderAsync(seed.OrderId, new DispatchOrderCommand("Carrier", null, null)));
    }

    private static EquipmentWorkflow CreateWorkflow(ERPFlowDbContext context, Guid userId) =>
        new(new EfEquipmentPersistence(context), new TestCurrentUser(userId), new ProductAuthorityWarehouseSourcePolicy(context));

    private static async Task<SeedResult> SeedApprovedOrderAsync(SqlServerTestDatabase database, int quantity)
    {
        await using var context = database.CreateContext();
        var customer = new Customer("Customer", "customer@example.test");
        var product = new Product("Machine", "MACHINE-1", 100m, 10);
        var administrator = new AppUser("Administrator", "admin@example.test", UserRole.Administrator, "test-hash");
        var technician = new AppUser("Technician", "technician@example.test", UserRole.Technician, "test-hash");
        var order = new SalesOrder(customer.Id, administrator.Id);
        order.AddItem(product, quantity);
        order.Submit([product]);
        order.Approve(administrator.Id);
        var hasOrigins = await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'SalesOrderItems') AND name=N'WarehouseId'").SingleAsync() != 0;
        if (hasOrigins)
        {
            context.AddRange(customer, product, administrator, technician, order);
            await context.SaveChangesAsync();
        }
        else
        {
            context.AddRange(customer, product, administrator, technician);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO SalesOrders(Id,CustomerId,CreatedByUserId,Status,CreatedAt,SubmittedAt,DecidedAt)
                VALUES({order.Id},{customer.Id},{administrator.Id},2,{order.CreatedAt},{order.SubmittedAt},{order.DecidedAt});
                INSERT INTO SalesOrderItems(Id,SalesOrderId,ProductId,Quantity,UnitPrice)
                VALUES({order.Items[0].Id},{order.Id},{product.Id},{quantity},{product.UnitPrice});
                INSERT INTO StockReservations(Id,SalesOrderId,ProductId,Quantity,IsReleased,CreatedAt)
                VALUES({order.Reservations[0].Id},{order.Id},{product.Id},{quantity},0,{order.Reservations[0].CreatedAt});
                """);
        }
        return new SeedResult(order.Id, product.Id, customer.Id, technician.Id);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task<DeliveryNoteResult>> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed record SeedResult(Guid OrderId, Guid ProductId, Guid CustomerId, Guid TechnicianId);

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
    }

    private sealed class ProductReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [Products]", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals) == 2)
                {
                    completion.TrySetResult();
                }

                await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private sealed class SqlServerTestDatabase(string connectionString) : IAsyncDisposable
    {
        public static async Task<SqlServerTestDatabase> CreateAsync(string? targetMigration = null)
        {
            var server = Environment.GetEnvironmentVariable("ERPFLOW_TEST_SQLSERVER") ?? @".\SQLEXPRESS";
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = $"ERPFlow_EquipmentTests_{Guid.NewGuid():N}",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true
            }.ConnectionString;
            var database = new SqlServerTestDatabase(connectionString);
            await using var context = database.CreateContext();
            await context.GetService<IMigrator>().MigrateAsync(targetMigration);
            return database;
        }

        public ERPFlowDbContext CreateContext(DbCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<ERPFlowDbContext>()
                .UseSqlServer(connectionString);
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }

            return new ERPFlowDbContext(options.Options);
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
