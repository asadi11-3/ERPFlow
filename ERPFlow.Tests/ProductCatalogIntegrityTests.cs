using ERPFlow.Application.Common;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.Products;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductCatalogIntegrityTests
{
    private static EfProductCatalog Catalog(ERPFlowDbContext context, Guid actor) => new(context, new InventoryTestDatabase.TestActor(actor));

    [Fact]
    public async Task PriceChangesPreserveSalesAndPurchaseSnapshotsAndFutureSalesUseNewPrice()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        Guid poId;
        await using (var setup = database.Context())
            poId = (await ProcurementTestSupport.Orders(setup, seed.Purchasing).CreateAsync(seed.Draft(cost: 7.1234m))).Id;
        await using (var edit = database.Context())
        {
            var catalog = Catalog(edit, seed.Inventory.ActorId);
            var before = await catalog.GetAsync(seed.Inventory.ProductId);
            await catalog.UpdateAsync(before.Id, new("New name", before.Sku, 99.99m, before.RowVersion));
        }
        await using var context = database.Context();
        Assert.All(await context.SalesOrderItems.ToListAsync(), item => Assert.Equal(10m, item.UnitPrice));
        Assert.All(await context.JobcardItems.ToListAsync(), item => Assert.Equal(10m, item.UnitPrice));
        Assert.Equal(7.1234m, (await context.PurchaseOrderItems.SingleAsync(x => x.PurchaseOrderId == poId)).UnitCost);
        var customer = await context.Customers.SingleAsync();
        var next = await StockTransferTestSupport.Sales(context, seed.Inventory.ActorId)
            .CreateDraftAsync(new(customer.Id, [new(seed.Inventory.ProductId, 1)]));
        Assert.Equal(99.99m, Assert.Single(next.Items).UnitPrice);
        var product = await context.Products.SingleAsync();
        Assert.Equal((50, 6), (product.StockOnHand, product.ReservedQuantity));
    }

    [Fact]
    public async Task DeactivationBlocksNewDraftsAndDraftSubmissionButPreservesCommittedSalesAndService()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        Guid draftPo;
        await using (var setup = database.Context())
            draftPo = (await ProcurementTestSupport.Orders(setup, seed.Purchasing).CreateAsync(seed.Draft())).Id;
        await using (var change = database.Context())
        {
            var catalog = Catalog(change, seed.Inventory.ActorId);
            var current = await catalog.GetAsync(seed.Inventory.ProductId);
            await catalog.SetActiveAsync(current.Id, new(current.RowVersion), false);
        }
        await using (var context = database.Context())
        {
            var customer = await context.Customers.SingleAsync();
            await Assert.ThrowsAsync<WorkflowConflictException>(() => StockTransferTestSupport.Sales(context, seed.Inventory.ActorId)
                .CreateDraftAsync(new(customer.Id, [new(seed.Inventory.ProductId, 1)])));
        }
        await using (var context = database.Context())
            await Assert.ThrowsAsync<WorkflowConflictException>(() => StockTransferTestSupport.Sales(context, seed.Inventory.ActorId)
                .SubmitAsync(seed.Inventory.OtherOrderId));
        await using (var context = database.Context())
            await Assert.ThrowsAsync<WorkflowConflictException>(() => ProcurementTestSupport.Orders(context, seed.Purchasing).CreateAsync(seed.Draft()));
        await using (var context = database.Context())
        {
            var orders = ProcurementTestSupport.Orders(context, seed.Purchasing);
            var po = await orders.GetAsync(draftPo);
            await Assert.ThrowsAsync<WorkflowConflictException>(() => orders.SubmitAsync(po.Id, new(po.RowVersion)));
        }
        Guid newJob;
        await using (var context = database.Context())
        {
            var customer = await context.Customers.SingleAsync();
            var service = StockTransferTestSupport.Service(context, seed.Technician);
            newJob = (await service.CreateJobcardAsync(new(customer.Id, null, "Tech", "Fault", ERPFlow.Domain.Enums.JobcardPriority.Medium))).Id;
            await Assert.ThrowsAsync<WorkflowConflictException>(() => service.RequestPartsAsync(newJob, new([new(seed.Inventory.ProductId, 1)])));
        }
        await using (var context = database.Context())
        {
            var service = StockTransferTestSupport.Service(context, seed.Technician);
            var existing = await context.JobcardItems.SingleAsync(x => x.Id == seed.Inventory.JobcardItemId);
            var completed = await service.CompleteJobcardAsync(existing.JobcardId, new("Done", null));
            Assert.NotNull(completed.CompletedAt);
        }
        await using (var context = database.Context())
        {
            var sales = StockTransferTestSupport.Sales(context, seed.Inventory.ActorId);
            await sales.ApproveAsync(seed.Inventory.SalesOrderId);
            var current = new InventoryTestDatabase.TestActor(seed.Inventory.ActorId);
            var equipment = new EquipmentWorkflow(new EfEquipmentPersistence(context), current,
                new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, current.UserId));
            var delivery = await equipment.DispatchOrderAsync(seed.Inventory.SalesOrderId, new("Carrier", null, null));
            Assert.NotEqual(Guid.Empty, delivery.Id);
        }
    }

    [Fact]
    public async Task SubmittedPurchaseCanBeApprovedAndReceivedAfterDeactivationWithoutRepricing()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using (var change = database.Context())
        {
            var catalog = Catalog(change, seed.Procurement.Inventory.ActorId);
            var current = await catalog.GetAsync(seed.Procurement.Inventory.ProductId);
            var edited = await catalog.UpdateAsync(current.Id, new("Renamed", current.Sku, 88, current.RowVersion));
            await catalog.SetActiveAsync(current.Id, new(edited.RowVersion), false);
            var created = await catalog.CreateAsync(new("New zero stock", "ZERO-WAREHOUSE", 0));
            Assert.True(created.IsActive);
        }
        await using (var receiving = database.Context())
        {
            var workflow = GoodsReceiptTestSupport.Workflow(receiving, seed.Procurement.Operator);
            var draft = await workflow.CreateAsync(seed.Draft(3));
            await workflow.PostAsync(draft.Id, new("post-inactive", draft.RowVersion));
        }
        await using var context = database.Context();
        var line = await context.PurchaseOrderItems.SingleAsync(x => x.PurchaseOrderId == seed.Order.Id);
        Assert.Equal(seed.Order.Items[0].UnitCost, line.UnitCost);
        Assert.Equal(3, line.ReceivedQuantity);
        var added = await context.Products.SingleAsync(x => x.Sku == "ZERO-WAREHOUSE");
        Assert.Equal((0, 0), (added.StockOnHand, added.ReservedQuantity));
        Assert.Equal(InventoryAuthorityMode.Warehouse, (await context.InventoryAuthorities.SingleAsync()).Mode);
    }

    [Fact]
    public async Task PendingPurchaseApprovalRemainsValidAfterProductDeactivation()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        ERPFlow.Application.Procurement.PurchaseOrderResult pending;
        await using (var setup = database.Context())
        {
            var orders = ProcurementTestSupport.Orders(setup, seed.Purchasing);
            var draft = await orders.CreateAsync(seed.Draft());
            pending = await orders.SubmitAsync(draft.Id, new(draft.RowVersion));
        }
        await using (var change = database.Context())
        {
            var catalog = Catalog(change, seed.Inventory.ActorId);
            var product = await catalog.GetAsync(seed.Inventory.ProductId);
            await catalog.SetActiveAsync(product.Id, new(product.RowVersion), false);
        }
        await using var approval = database.Context();
        var approved = await ProcurementTestSupport.Orders(approval, seed.Approver).ApproveAsync(pending.Id, new(pending.RowVersion));
        Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task InactiveProductBlocksNewTransfersButExistingDraftCanStillPost()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database, reserve: false);
        ERPFlow.Application.Transfers.StockTransferResult draft;
        await using (var setup = database.Context())
            draft = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("before-inactive", seed.Draft());
        await using (var change = database.Context())
        {
            var catalog = Catalog(change, seed.Procurement.Inventory.ActorId);
            var product = await catalog.GetAsync(seed.Product);
            await catalog.SetActiveAsync(product.Id, new(product.RowVersion), false);
        }
        await using (var rejected = database.Context())
            await Assert.ThrowsAsync<WorkflowConflictException>(() => StockTransferTestSupport.Workflow(rejected, seed.Procurement.Operator)
                .CreateAsync("after-inactive", seed.Draft()));
        await using var posting = database.Context();
        var result = await StockTransferTestSupport.Workflow(posting, seed.Procurement.Operator)
            .PostAsync(draft.Id, "existing-inactive", new(draft.RowVersion));
        Assert.Equal(ERPFlow.Domain.Transfers.StockTransferStatus.Posted, result.Status);
    }

    [Fact]
    public async Task TwoConcurrentCatalogWritersHaveOneWinnerAndOneConflict()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var first = database.Context();
        await using var second = database.Context();
        var version = (await Catalog(first, seed.ActorId).GetAsync(seed.ProductId)).RowVersion;
        async Task<bool> Update(ERPFlowDbContext context, string name)
        {
            try { await Catalog(context, seed.ActorId).UpdateAsync(seed.ProductId, new(name, "INVENTORY", 20, version)); return true; }
            catch (WorkflowConflictException) { return false; }
        }
        var results = await Task.WhenAll(Update(first, "First"), Update(second, "Second"));
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
    }

    [Fact]
    public void MigrationIsAdditiveAndModelIsConsistent()
    {
        using var context = new ERPFlowDbContext(new DbContextOptionsBuilder<ERPFlowDbContext>()
            .UseSqlServer("Server=unreachable;Database=DesignOnly;Integrated Security=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Value.Name == "ProductCatalogPhase6A2");
        var migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.Single(migration.UpOperations.OfType<AddColumnOperation>(), op => op.Table == "Products" && op.Name == "IsActive" && Equals(op.DefaultValue, true));
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(), op => op.Name == "ProductCatalogChanges");
        Assert.DoesNotContain(migration.UpOperations, op => op is AlterColumnOperation or DropTableOperation or DropColumnOperation or UpdateDataOperation);
        Assert.Throws<NotSupportedException>(() => { _ = migration.DownOperations; });
    }
}
