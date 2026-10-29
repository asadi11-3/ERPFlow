using ERPFlow.Api;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.ProductImages;
using ERPFlow.Application.Receiving;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure;
using ERPFlow.Infrastructure.Deployment;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using ERPFlow.Infrastructure.ProductImages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductionDeploymentTests
{
    [Fact]
    public async Task ProductionCompositionPostsReceiptsTransfersReservationsDispatchAndServiceWithReconciliation()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database, quantity: 20);
        await using var setup = database.Context();
        await using var provider = Services(setup.Database.GetConnectionString()!, seed.Procurement.Inventory.ActorId);
        var admission = provider.GetRequiredService<ProductionInventoryAdmission>();
        await admission.InitializeAsync(setup);
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.IsType<ProductionWarehouseWriteGate>(scope.ServiceProvider.GetRequiredService<IInventoryWriteGate>());
            Assert.IsType<DisabledInventoryCutoverPermission>(scope.ServiceProvider.GetRequiredService<IInventoryCutoverPermission>());
            Assert.Throws<WorkflowConflictException>(() => scope.ServiceProvider.GetRequiredService<IInventoryCutoverPermission>().EnsureOperatorExecutionAllowed());
            var receipts = scope.ServiceProvider.GetRequiredService<IGoodsReceiptWorkflow>();
            var draft = await receipts.CreateAsync(seed.Draft(10, "production-grn"));
            var posted = await receipts.PostAsync(draft.Id, new("production-grn-post", draft.RowVersion));
            var replay = await receipts.PostAsync(draft.Id, new("production-grn-post", draft.RowVersion));
            Assert.Equal(posted.Id, replay.Id);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var transfers = scope.ServiceProvider.GetRequiredService<IStockTransferWorkflow>();
            var draft = await transfers.CreateAsync("production-transfer", new(seed.Order.WarehouseId, seed.Procurement.Inventory.OtherWarehouseId,
                [new(seed.Procurement.Inventory.ProductId, 4)]));
            await transfers.PostAsync(draft.Id, "production-transfer-post", new(draft.RowVersion));
            await transfers.PostAsync(draft.Id, "production-transfer-post", new(draft.RowVersion));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
            var customer = await db.Customers.SingleAsync();
            var sales = scope.ServiceProvider.GetRequiredService<ISalesOrderWorkflow>();
            var order = await sales.CreateDraftAsync(new(customer.Id, [new(seed.Procurement.Inventory.ProductId, 2, seed.Order.WarehouseId)]));
            await sales.SubmitAsync(order.Id);
            await sales.ApproveAsync(order.Id);
            await scope.ServiceProvider.GetRequiredService<IEquipmentWorkflow>().DispatchOrderAsync(order.Id, new("Audit carrier", null, null));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
            var customer = await db.Customers.SingleAsync();
            var service = scope.ServiceProvider.GetRequiredService<IServiceWorkflow>();
            var job = await service.CreateJobcardAsync(new(customer.Id, null, "Production technician", "Owned test", JobcardPriority.Medium));
            await service.RequestPartsAsync(job.Id, new([new(seed.Procurement.Inventory.ProductId, 1, seed.Procurement.Inventory.OtherWarehouseId)]));
            await service.CompleteJobcardAsync(job.Id, new("Completed", null));
        }
        await new EfInventoryReconciliation(setup).ReconcileAsync();
        var source = await setup.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == seed.Order.WarehouseId);
        var destination = await setup.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == seed.Procurement.Inventory.OtherWarehouseId);
        Assert.Equal(4, source.StockOnHand);
        Assert.Equal(3, destination.StockOnHand);
        Assert.Equal(0, source.ReservedQuantity);
        Assert.Equal(0, destination.ReservedQuantity);
        Assert.Equal(50, (await setup.Products.SingleAsync()).StockOnHand);
        Assert.Single(await setup.DeliveryNotes.ToArrayAsync());
        Assert.Equal(2, await setup.MachineAssets.CountAsync());
        Assert.Equal(1, await setup.GoodsReceipts.CountAsync(x => x.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Posted));
    }

    [Fact]
    public async Task UninitializedProductAuthorityAndWrongDatabaseCannotObtainWriteAdmission()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        await using var context = database.Context();
        var admission = new ProductionInventoryAdmission();
        var gate = new ProductionWarehouseWriteGate(context, admission);
        Assert.Throws<WorkflowConflictException>(gate.EnsureWarehouseWritesAllowed);
        await admission.InitializeAsync(context);
        Assert.Throws<WorkflowConflictException>(gate.EnsureWarehouseWritesAllowed);
        Assert.False(await admission.IsReadyAsync(context, default));
        await GoodsReceiptTestSupport.SeedAsync(database);
        await admission.InitializeAsync(context);
        gate.EnsureWarehouseWritesAllowed();
        await using var other = await InventoryTestDatabase.CreateAsync();
        await using var otherContext = other.Context();
        Assert.Throws<WorkflowConflictException>(() => new ProductionWarehouseWriteGate(otherContext, admission).EnsureWarehouseWritesAllowed());
        await using var unfenced = await context.Database.BeginTransactionAsync();
        Assert.Throws<WorkflowConflictException>(gate.EnsureWarehouseWritesAllowed);
    }

    [Fact]
    public async Task RuntimeSchemaCheckRejectsOldSchemaAndDisabledGuardWithoutApplyingMigrations()
    {
        await using var old = await InventoryTestDatabase.CreateAsync(InventoryTestDatabase.LegacyMigration);
        await using var oldContext = old.Context();
        var before = (await oldContext.Database.GetAppliedMigrationsAsync()).ToArray();
        await Assert.ThrowsAsync<WorkflowConflictException>(() => new DatabaseSchemaVerifier(oldContext).VerifyAsync());
        Assert.Equal(before, (await oldContext.Database.GetAppliedMigrationsAsync()).ToArray());
        await using var current = await InventoryTestDatabase.CreateAsync();
        await using var context = current.Context();
        await context.Database.ExecuteSqlRawAsync("DISABLE TRIGGER TR_Products_FrozenLegacyBalances ON Products;");
        await Assert.ThrowsAsync<WorkflowConflictException>(() => new ProductionInventoryAdmission().InitializeAsync(context));
    }

    [Fact]
    public async Task AdmissionRejectsUnreconciledWarehouseLedger()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        await using (var tx = await InventoryAuthoritySession.BeginAsync(context))
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE WarehouseStocks SET StockOnHand=StockOnHand+1 WHERE WarehouseId=(SELECT DefaultWarehouseId FROM InventoryAuthorities WHERE Id=1)");
            await tx.CommitAsync();
        }
        var admission = new ProductionInventoryAdmission();
        await Assert.ThrowsAsync<WorkflowConflictException>(() => admission.InitializeAsync(context));
        Assert.Throws<WorkflowConflictException>(() => new ProductionWarehouseWriteGate(context, admission).EnsureWarehouseWritesAllowed());
    }

    [Fact]
    public async Task AdmittedProcessRefusesWritesIfSqlGuardsAreDisabledLater()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var admission = new ProductionInventoryAdmission();
        await admission.InitializeAsync(context);
        var gate = new ProductionWarehouseWriteGate(context, admission);
        gate.EnsureWarehouseWritesAllowed();
        await context.Database.ExecuteSqlRawAsync("DISABLE TRIGGER TR_Products_FrozenLegacyBalances ON Products;");
        Assert.Throws<WorkflowConflictException>(gate.EnsureWarehouseWritesAllowed);
    }

    [Fact]
    public async Task RuntimeVerificationAndReceivingWorkWithoutDdlPermission()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE USER ERPFlowAuditRuntime WITHOUT LOGIN;
            GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE, VIEW DEFINITION TO ERPFlowAuditRuntime;
            EXECUTE AS USER='ERPFlowAuditRuntime';
            """);
        try
        {
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT CONVERT(int,HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER')) AS Value").SingleAsync());
            var admission = new ProductionInventoryAdmission();
            await admission.InitializeAsync(context);
            var gate = new ProductionWarehouseWriteGate(context, admission);
            var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator, gate);
            var draft = await workflow.CreateAsync(seed.Draft(1, "limited-runtime-create"));
            await workflow.PostAsync(draft.Id, new("limited-runtime-post", draft.RowVersion));
            await new EfInventoryReconciliation(context).ReconcileAsync();
        }
        finally { await context.Database.ExecuteSqlRawAsync("REVERT;"); }
    }

    [Fact]
    public async Task FirstAdministratorIsHashedAndCannotBeRepeatedOrOverwriteAnExistingUser()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        await using var context = database.Context();
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var provisioner = new FirstAdministratorProvisioner(context);
        var id = await provisioner.ProvisionAsync("Owner", "owner@production.test", password);
        var user = await context.AppUsers.AsNoTracking().SingleAsync();
        Assert.Equal(id, user.Id);
        Assert.Equal(UserRole.Administrator, user.Role);
        Assert.NotEqual(password, user.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<object>().VerifyHashedPassword(new object(), user.PasswordHash, password));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => provisioner.ProvisionAsync("Other", "other@production.test", password));
        Assert.Single(await context.AppUsers.ToArrayAsync());
    }

    [Fact]
    public void LoginThrottleIsAccountScopedAndExpiresWithoutResettingOnSuccess()
    {
        var clock = new MutableClock();
        var limiter = new LoginAttemptLimiter(clock);
        for (var i = 0; i < 5; i++) Assert.True(limiter.TryAcquire("Owner@Example.test"));
        Assert.False(limiter.TryAcquire(" owner@example.test "));
        Assert.True(limiter.TryAcquire("other@example.test"));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(limiter.TryAcquire("owner@example.test"));
    }

    private static ServiceProvider Services(string connection, Guid actor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new InventoryTestDatabase.TestActor(actor));
        services.AddSingleton<IProductImageStorage>(_ => new LocalProductImageStorage(new()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "ERPFlow-UnusedProductionTest-" + Guid.NewGuid().ToString("N"))
        }));
        services.AddInfrastructure(_ => connection).AddProductionInventory();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
